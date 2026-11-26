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
    public void Without_cloud_the_local_keys_work_as_before()
    {
        var s = new AiSettingsStore(_dir);
        s.UseCloud(() => null);   // chưa nối cloud / chưa biết trạng thái
        Assert.False(s.UsesCloud);
        Assert.False(s.HasKey);
        s.SaveKey(AiSettingsStore.Groq, "khoa-may");
        Assert.True(s.HasKey);
        Assert.All(s.BuildEndpoints(), e => Assert.Equal("khoa-may", e.ApiKey));
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
