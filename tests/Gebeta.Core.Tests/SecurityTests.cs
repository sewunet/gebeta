using Gebeta.Core.Domain.Security;

namespace Gebeta.Core.Tests;

public class PermissionTests
{
    [Theory]
    [InlineData("*", "payroll.salary-slips.submit", true)]
    [InlineData("core.*", "core.companies.create", true)]
    [InlineData("core.*", "payroll.salary-slips.read", false)]
    [InlineData("core.companies.*", "core.companies.create", true)]
    [InlineData("core.companies.*", "core.users.read", false)]
    [InlineData("core.companies.read", "core.companies.read", true)]
    [InlineData("core.companies.read", "core.companies.create", false)]
    [InlineData("core.comp.*", "core.companies.read", false)]
    public void Wildcards_cover_only_their_prefix(string granted, string required, bool expected)
    {
        Assert.Equal(expected, PermissionPattern.Matches(granted, required));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("core.*", true)]
    [InlineData("core.companies.*", true)]
    [InlineData("payroll.salary-slips.submit", true)]
    [InlineData("core.companies", false)]
    [InlineData("core.companies.read.extra", false)]
    [InlineData("core..read", false)]
    [InlineData("core.companies.re ad", false)]
    [InlineData("", false)]
    public void Validates_the_permission_format(string permission, bool valid)
    {
        Assert.Equal(valid, PermissionPattern.IsValidPattern(permission));
    }
}

public class RoleTests
{
    [Fact]
    public void A_role_allows_what_it_grants()
    {
        var role = Role.Create("Accountant").Value;
        role.Grant("accounting.*");
        role.Grant("core.companies.read");

        Assert.True(role.Allows("accounting.journal-entries.submit"));
        Assert.True(role.Allows("core.companies.read"));
        Assert.False(role.Allows("core.companies.create"));
    }

    [Fact]
    public void Granting_is_idempotent_and_revoking_removes()
    {
        var role = Role.Create("Clerk").Value;
        role.Grant("core.companies.read");
        role.Grant("CORE.companies.read");

        Assert.Single(role.Permissions);

        role.Revoke("core.companies.read");
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void Invalid_permissions_are_refused()
    {
        var role = Role.Create("Clerk").Value;

        Assert.True(role.Grant("companies").IsFailure);
        Assert.Empty(role.Permissions);
    }
}

public class UserTests
{
    [Fact]
    public void A_user_is_created_from_the_identity_provider_with_ethiopian_friendly_defaults()
    {
        var user = User.Create("kc-123", "Abebe.Kebede", "abebe@example.et", "Abebe Kebede").Value;

        Assert.Equal("abebe.kebede", user.Username);
        Assert.Equal("en", user.Language);
        Assert.Equal(CalendarPreference.Both, user.Calendar);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Language_is_english_or_amharic()
    {
        var user = User.Create("kc-1", "almaz", null, null).Value;

        Assert.True(user.SetPreferences("am", CalendarPreference.Ethiopian, null).IsSuccess);
        Assert.True(user.SetPreferences("fr", CalendarPreference.Both, null).IsFailure);
        Assert.Equal("am", user.Language);
    }

    [Fact]
    public void Roles_are_assigned_once_and_can_be_removed()
    {
        var user = User.Create("kc-2", "dawit", null, null).Value;
        var role = Role.Create("Clerk").Value;

        user.AssignRole(role);
        user.AssignRole(role);
        Assert.Single(user.RoleIds);

        user.RemoveRole(role.Id);
        Assert.Empty(user.RoleIds);
    }
}
