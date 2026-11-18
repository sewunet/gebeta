using System.Net;
using System.Net.Http.Json;

namespace Gebeta.Api.Tests;

/// <summary>The amount in words every voucher prints, from one place (SharedKernel) for all modules.</summary>
public class AmountInWordsApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Words(string English, string Amharic);

    [Fact]
    public async Task Any_signed_in_user_gets_an_amount_in_english_and_amharic()
    {
        using var user = factory.ClientFor("words.user");
        var words = (await user.GetFromJsonAsync<Words>(new Uri("/api/core/amount-in-words?amount=2475.25", UriKind.Relative), Ct))!;
        Assert.Equal("Two thousand four hundred seventy-five birr and twenty-five santim only", words.English);
        Assert.Equal("ሁለት ሺህ አራት መቶ ሰባ አምስት ብር ከሃያ አምስት ሳንቲም", words.Amharic);

        var tooLarge = await user.GetAsync(new Uri("/api/core/amount-in-words?amount=1000000000000", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        using var anonymous = factory.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/core/amount-in-words?amount=1", UriKind.Relative), Ct)).StatusCode);
    }
}
