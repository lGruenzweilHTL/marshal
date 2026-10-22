using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Marshal.Api.Tests.Auth;

public sealed class PolicyCredentialsTests
{
    [Fact]
    public void ScopePolicy_AcceptsATokenWithTheScopeOrASession()
    {
        PolicyCredentials credentials = Policies.Credentials(ApiScopes.TasksRead);

        Assert.True(credentials.Session);
        Assert.Equal([ApiScopes.TasksRead], credentials.TokenScopes);
    }

    [Fact]
    public void HumanPolicy_AcceptsTheSessionOnly()
    {
        PolicyCredentials credentials = Policies.Credentials(Policies.Human);

        Assert.True(credentials.Session);
        Assert.Null(credentials.TokenScopes);
    }

    [Fact]
    public void HumanOrAdminPolicy_AcceptsASessionOrAnAdminToken()
    {
        PolicyCredentials credentials = Policies.Credentials(Policies.HumanOrAdmin);

        Assert.True(credentials.Session);
        Assert.Equal([ApiScopes.Admin], credentials.TokenScopes);
    }

    [Fact]
    public void DefaultPolicy_AcceptsAnyTokenOrASession()
    {
        PolicyCredentials credentials = Policies.Credentials(policyName: null);

        Assert.True(credentials.Session);
        Assert.Empty(credentials.TokenScopes!);
    }

    [Fact]
    public void UnknownPolicyName_Throws()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Policies.Credentials("made-up"));

        Assert.Contains("made-up", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenSchemePolicy_AcceptsAnyTokenAndNoSession()
    {
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder(MarshalAuthSchemes.ApiToken).RequireAuthenticatedUser().Build();

        PolicyCredentials credentials = Policies.Credentials(policy);

        Assert.False(credentials.Session);
        Assert.Empty(credentials.TokenScopes!);
    }

    [Fact]
    public void PolicyForAnotherScheme_Throws()
    {
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder(MarshalAuthSchemes.ExecutorCallback).RequireAuthenticatedUser().Build();

        Assert.Throws<InvalidOperationException>(() => Policies.Credentials(policy));
    }

    [Fact]
    public void Combine_KeepsOnlyCredentialsEveryPartAccepts_WithAllScopes()
    {
        PolicyCredentials humanAndAdmin = Policies.Credentials(Policies.Human).Combine(Policies.Credentials(Policies.Admin));
        PolicyCredentials twoScopes = Policies.Credentials(ApiScopes.TasksRead).Combine(Policies.Credentials(ApiScopes.TasksWrite));
        PolicyCredentials tokenOnly = Policies.Credentials(policyName: null)
            .Combine(Policies.Credentials(new AuthorizationPolicyBuilder(MarshalAuthSchemes.ApiToken).RequireAuthenticatedUser().Build()));

        Assert.Equal((true, (IReadOnlyList<string>?)null), (humanAndAdmin.Session, humanAndAdmin.TokenScopes));
        Assert.Equal([ApiScopes.TasksRead, ApiScopes.TasksWrite], twoScopes.TokenScopes);
        Assert.True(twoScopes.Session);
        Assert.False(tokenOnly.Session);
        Assert.Empty(tokenOnly.TokenScopes!);
    }
}
