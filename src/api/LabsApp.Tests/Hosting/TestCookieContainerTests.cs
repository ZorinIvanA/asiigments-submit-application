namespace LabsApp.Tests.Hosting;

/// <summary>
/// Unit-тесты ручного cookie-контейнера для сквозных сценариев (AC T-004):
/// захват Set-Cookie, перезапись/удаление, формирование заголовка Cookie запроса.
/// </summary>
public sealed class TestCookieContainerTests
{
    [Fact]
    public void CaptureFrom_SingleSetCookie_StoresNameAndValue()
    {
        var container = new TestCookieContainer();
        using var response = new HttpResponseMessage();
        response.Headers.Add("Set-Cookie", "access_token=abc.def; Path=/; HttpOnly; SameSite=Strict");

        container.CaptureFrom(response);

        Assert.True(container.Contains("access_token"));
        Assert.Equal("abc.def", container.GetValue("access_token"));
        Assert.Equal(1, container.Count);
    }

    [Fact]
    public void CaptureFrom_MultipleCookies_StoresAll()
    {
        var container = new TestCookieContainer();
        using var response = new HttpResponseMessage();
        response.Headers.Add("Set-Cookie", "access_token=token-value; Path=/");
        response.Headers.Add("Set-Cookie", "session_hint=hint-value; Path=/");

        container.CaptureFrom(response);

        Assert.Equal("token-value", container.GetValue("access_token"));
        Assert.Equal("hint-value", container.GetValue("session_hint"));
        Assert.Equal(2, container.Count);
    }

    [Fact]
    public void CaptureFrom_ResponseWithoutSetCookie_LeavesContainerEmpty()
    {
        var container = new TestCookieContainer();
        using var response = new HttpResponseMessage();

        container.CaptureFrom(response);

        Assert.Equal(0, container.Count);
    }

    [Fact]
    public void CaptureFrom_RepeatedSetCookie_ReplacesValue()
    {
        var container = new TestCookieContainer();
        using var first = new HttpResponseMessage();
        first.Headers.Add("Set-Cookie", "access_token=old; Path=/");
        using var second = new HttpResponseMessage();
        second.Headers.Add("Set-Cookie", "access_token=new; Path=/");

        container.CaptureFrom(first);
        container.CaptureFrom(second);

        Assert.Equal("new", container.GetValue("access_token"));
        Assert.Equal(1, container.Count);
    }

    [Fact]
    public void CaptureFrom_EmptyValue_RemovesCookie()
    {
        var container = new TestCookieContainer();
        using var set = new HttpResponseMessage();
        set.Headers.Add("Set-Cookie", "access_token=value; Path=/");
        container.CaptureFrom(set);
        using var clear = new HttpResponseMessage();
        clear.Headers.Add("Set-Cookie", "access_token=; Path=/; Max-Age=0");

        container.CaptureFrom(clear);

        Assert.False(container.Contains("access_token"));
    }

    [Fact]
    public void CaptureFrom_MaxAgeZero_RemovesCookie()
    {
        var container = new TestCookieContainer();
        using var set = new HttpResponseMessage();
        set.Headers.Add("Set-Cookie", "access_token=value; Path=/");
        container.CaptureFrom(set);
        using var clear = new HttpResponseMessage();
        clear.Headers.Add("Set-Cookie", "access_token=value; Path=/; Max-Age=0");

        container.CaptureFrom(clear);

        Assert.False(container.Contains("access_token"));
    }

    [Fact]
    public void ApplyTo_SetsCookieHeaderOnRequest()
    {
        var container = new TestCookieContainer();
        container.Set("access_token", "token-value");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");

        container.ApplyTo(request);

        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Equal("access_token=token-value", header);
    }

    [Fact]
    public void ApplyTo_MultipleCookies_JoinedInHeader()
    {
        var container = new TestCookieContainer();
        container.Set("access_token", "token-value");
        container.Set("refresh_hint", "hint-value");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");

        container.ApplyTo(request);

        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("access_token=token-value", header, StringComparison.Ordinal);
        Assert.Contains("refresh_hint=hint-value", header, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTo_MergesWithExistingHeader_ContainerPairsWin_ForeignPairsKept()
    {
        var container = new TestCookieContainer();
        container.Set("access_token", "new-value");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", "access_token=stale-value; csrf=keep-me");

        container.ApplyTo(request);

        var header = Assert.Single(request.Headers.GetValues("Cookie"));
        Assert.Contains("access_token=new-value", header, StringComparison.Ordinal);
        Assert.Contains("csrf=keep-me", header, StringComparison.Ordinal);
        Assert.DoesNotContain("stale-value", header, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTo_EmptyContainerWithoutExistingHeader_DoesNotSetHeader()
    {
        var container = new TestCookieContainer();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");

        container.ApplyTo(request);

        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public void ManualManagement_SetRemoveClear_RoundTrip()
    {
        var container = new TestCookieContainer();

        container.Set("access_token", "one");
        Assert.True(container.Contains("access_token"));
        container.Set("access_token", "two");
        Assert.Equal("two", container.GetValue("access_token"));

        Assert.True(container.Remove("access_token"));
        Assert.False(container.Remove("access_token"));
        Assert.Null(container.GetValue("access_token"));

        container.Set("a", "1");
        container.Set("b", "2");
        container.Clear();
        Assert.Equal(0, container.Count);
    }
}
