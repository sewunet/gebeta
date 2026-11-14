using System.Security.Claims;
using Gebeta.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gebeta.Modularity;

public static class AuthorizationExtensions
{
    private const string PolicyPrefix = "perm:";

    /// <summary>
    /// Requires the signed-in user to hold <paramref name="permission"/> (e.g. <c>core.companies.create</c>).
    /// Returns 401 when not signed in and 403 when the permission is missing.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PolicyPrefix + permission);

    /// <summary>Permission policies, the current user from the token, and "signed in" as the default for every endpoint.</summary>
    public static IServiceCollection AddGebetaAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
    {
        public string Permission { get; } = permission;
    }

    private sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
    {
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            {
                return new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
                    .Build();
            }

            return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        }
    }

    private sealed class PermissionHandler(IPermissionChecker checker) : AuthorizationHandler<PermissionRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true
                && await checker.HasPermissionAsync(requirement.Permission, CancellationToken.None).ConfigureAwait(false))
            {
                context.Succeed(requirement);
            }
        }
    }

    /// <summary>Reads the Keycloak claims (raw names: sub, preferred_username, email, name).</summary>
    private sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public string? ExternalId => Claim("sub");
        public string? Username => Claim("preferred_username") ?? ExternalId;
        public string? Email => Claim("email");
        public string? FullName => Claim("name");

        private string? Claim(string type) => Principal?.FindFirst(type)?.Value;
    }
}
