using System.IO;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Chế độ cloud: AI đi qua cổng AI của cloud (khóa Groq/Gemini chỉ nằm trên cloud), máy dùng token thiết bị. Dữ liệu GIẢ.</summary>
public sealed class AiCloudKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-aicloud-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Cloud_access_uses_the_proxy_with_the_device_token_and_ignores_local_keys()
    {
        var s = new AiSettingsStore(_dir);
        s.SaveKey(AiSettingsStore.Groq, "khoa-may-cu");
        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "token-thiet-bi", Groq: true, Gemini: true));

        Assert.True(s.UsesCloud);
        Assert.True(s.HasKey);
        var eps = s.BuildEndpoints();
        Assert.NotEmpty(eps);
        Assert.All(eps, e => Assert.Equal("token-thiet-bi", e.ApiKey));
        Assert.Contains(eps, e => e.BaseUrl == "https://x.workers.dev/v1/ai/groq");
        Assert.Contains(eps, e => e.BaseUrl == "https://x.workers.dev/v1/ai/gemini");
        Assert.True(eps.FindIndex(e => e.Provider == "groq") < eps.FindIndex(e => e.Provider == "gemini"));   // Groq trước như cũ
    }

    [Fact]
    public void Only_providers_configured_on_the_cloud_are_used()
    {
        var s = new AiSettingsStore(_dir);
        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "tok", Groq: false, Gemini: true));
        Assert.All(s.BuildEndpoints(), e => Assert.Equal("gemini", e.Provider));

        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "tok", Groq: false, Gemini: false));
        Assert.False(s.HasKey);
        Assert.Empty(s.BuildEndpoints());
    }

    [Fact]
    public void Without_cloud_mode_the_local_keys_work_as_before()
    {
        var s = new AiSettingsStore(_dir);
        Assert.False(s.CloudMode);
        s.SaveKey(AiSettingsStore.Groq, "khoa-may");
        Assert.True(s.HasKey);
        Assert.All(s.BuildEndpoints(), e => Assert.Equal("khoa-may", e.ApiKey));
    }

    [Fact]
    public void Cloud_mode_never_uses_local_keys_even_before_the_cloud_answered()
    {
        // chủ dự án 26/11/2026: chế độ cloud không cho nhập/dùng khóa trên máy, kể cả lúc chưa hỏi được cloud
        var s = new AiSettingsStore(_dir);
        s.SaveKey(AiSettingsStore.Groq, "khoa-may-cu");
        s.UseCloud(() => null);
        Assert.True(s.CloudMode);
        Assert.False(s.UsesCloud);
        Assert.False(s.HasKey);
        Assert.Empty(s.BuildEndpoints());
    }
}

public sealed class AiGeminiModelTests
{
    [Fact]
    public void Retired_gemini_models_are_dropped_and_an_all_retired_list_falls_back_to_the_default()
    {
        Assert.Equal("gemini-4-hydrogen, gemini-3.5-flash-lite", AiSettingsStore.CleanGeminiModels("gemini-2.5-flash, gemini-4-hydrogen, gemini-1.5-pro, gemini-3.5-flash-lite"));
        Assert.Equal(QuanLyHangHoan.Data.Ai.AiChatService.DefaultGeminiModels, AiSettingsStore.CleanGeminiModels("gemini-2.5-flash, gemini-2.5-flash-lite"));
        Assert.DoesNotContain("gemini-2.", QuanLyHangHoan.Data.Ai.AiChatService.DefaultGeminiModels);
    }

    [Fact]
    public void Loading_a_saved_list_with_retired_models_cleans_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-gem-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new AiSettingsStore(dir);
            s.SaveModel(AiSettingsStore.Gemini, "gemini-2.5-flash, gemini-2.5-flash-lite");
            Assert.Equal(QuanLyHangHoan.Data.Ai.AiChatService.DefaultGeminiModels, s.LoadModel(AiSettingsStore.Gemini));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

/// <summary>NVIDIA NIM = tuyến AI dự phòng cuối (chủ dự án 26/11/2026): Groq -> Google -> NVIDIA.</summary>
public sealed class AiNvidiaFallbackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-nv-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Cloud_chain_puts_nvidia_last()
    {
        var s = new AiSettingsStore(_dir);
        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "tok", Groq: true, Gemini: true, Nvidia: true));
        var eps = s.BuildEndpoints();
        Assert.Equal("nvidia", eps[^1].Provider);
        Assert.Equal("https://x.workers.dev/v1/ai/nvidia", eps[^1].BaseUrl);
        Assert.Equal("tok", eps[^1].ApiKey);
        Assert.True(eps.FindLastIndex(e => e.Provider == "gemini") < eps.FindIndex(e => e.Provider == "nvidia"));
    }

    [Fact]
    public void Nvidia_alone_is_enough_and_local_nvidia_key_works_without_cloud()
    {
        var s = new AiSettingsStore(_dir);
        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "tok", Groq: false, Gemini: false, Nvidia: true));
        Assert.True(s.HasKey);
        Assert.All(s.BuildEndpoints(), e => Assert.Equal("nvidia", e.Provider));

        var local = new AiSettingsStore(Path.Combine(_dir, "local"));
        local.SaveKey(AiSettingsStore.Nvidia, "nv-key");
        Assert.True(local.HasKey);
        var ep = Assert.Single(local.BuildEndpoints().Where(e => e.Model == "nvidia/nemotron-3-ultra-550b-a55b"));
        Assert.Equal(AiChatService.NvidiaBaseUrl, ep.BaseUrl);
        Assert.Equal("nv-key", ep.ApiKey);
    }
}

public sealed class AiCloudflareFallbackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-cf-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Chain_order_is_groq_google_nvidia_then_cloudflare()
    {
        var s = new AiSettingsStore(_dir);
        s.UseCloud(() => new CloudAiAccess("https://x.workers.dev/v1", "tok", true, true, true, true));
        var providers = s.BuildEndpoints().Select(e => e.Provider).Distinct().ToArray();
        Assert.Equal(new[] { "groq", "gemini", "nvidia", "cloudflare" }, providers);
        var cf = s.BuildEndpoints().Last();
        Assert.Equal("https://x.workers.dev/v1/ai/cloudflare", cf.BaseUrl);
        Assert.Equal("@cf/meta/llama-4-scout-17b-16e-instruct", cf.Model);
    }

    [Fact]
    public void Cloudflare_is_only_used_through_the_cloud()
    {
        var s = new AiSettingsStore(_dir);
        s.SaveKey(AiSettingsStore.Groq, "g");
        Assert.DoesNotContain(s.BuildEndpoints(), e => e.Provider == "cloudflare");
    }
}

public sealed class AiRetiredModelTests
{
    [Fact]
    public void Retired_nvidia_and_cloudflare_models_are_dropped()
    {
        Assert.Equal(AiChatService.DefaultNvidiaModels, AiSettingsStore.CleanModels(AiSettingsStore.Nvidia, "meta/llama-3.3-70b-instruct"));
        Assert.Equal("@cf/openai/gpt-oss-120b", AiSettingsStore.CleanModels(AiSettingsStore.Cloudflare, "@cf/meta/llama-3.3-70b-instruct-fp8-fast, @cf/openai/gpt-oss-120b"));
        Assert.Equal("openai/gpt-oss-120b", AiSettingsStore.CleanModels(AiSettingsStore.Groq, "openai/gpt-oss-120b"));
    }
}
