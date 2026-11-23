using System.Media;

namespace QuanLyHangHoan.App.Services;

public interface IScanSoundPlayer
{
    void PlaySuccess();
    void PlayError();
}

public sealed class SystemScanSoundPlayer : IScanSoundPlayer
{
    public void PlaySuccess() => SystemSounds.Asterisk.Play();
    public void PlayError() => SystemSounds.Hand.Play();
}

public sealed class NoOpScanSoundPlayer : IScanSoundPlayer
{
    public void PlaySuccess() { }
    public void PlayError() { }
}
