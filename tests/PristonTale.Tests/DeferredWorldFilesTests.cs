using PristonTale.Gameplay.World;

namespace PristonTale.Tests;

internal static class DeferredWorldFilesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("browser world entry keeps the field's files and the entering class's motions, and defers the rest", RulesAsync);
        yield return ("deferred world files start with the field's first frame and settle even when their download fails", LifecycleAsync);
    }

    private static Task RulesAsync()
    {
        const string Own = "assets/char/tmABCD/m4.smb";
        Assert.True(DeferredWorldFiles.FullMotionBank("assets/char/tmABCD/m4.preview.smb") == Own
            && DeferredWorldFiles.FullMotionBank("assets/char/tmABCD/M4Bip.inx") is null, "the full bank was not derived from the preview skeleton");
        Assert.True(DeferredWorldFiles.IsFullMotionBank(Own) && !DeferredWorldFiles.IsFullMotionBank("assets/char/tmABCD/m4.preview.smb")
            && !DeferredWorldFiles.IsFullMotionBank("assets/char/npc/MN-012/m1.smb"), "a full motion bank was misidentified");

        foreach (var path in new[] { "assets/char/tmABCD/m1.smb", "assets/Field/map/fore-1.tga", "assets/image/GuideMap/a.bmp",
            "assets/cSelect/Login.smd", "assets/StartImage/login/Loading/1.tga" })
        {
            Assert.True(DeferredWorldFiles.CanWait(path, Own), $"{path} held up world entry");
        }

        foreach (var path in new[] { Own, "assets/Field/map/village-2.tga", "assets/StartImage/MessageBox/ChatBox/box.tga",
            "assets/Field/Ricarten/village-2.smd", "assets/char/npc/MN-012/MN-012.inx", "assets/image/Sinimage/Inter/a.bmp", "Content/x.xnb" })
        {
            Assert.True(!DeferredWorldFiles.CanWait(path, Own), $"{path}, which the field reads on entry, was deferred");
        }

        Assert.True(!DeferredWorldFiles.CanWait("assets/char/tmABCD/m1.smb", null), "an unknown class did not keep every motion bank");
        return Task.CompletedTask;
    }

    private static async Task LifecycleAsync()
    {
        DeferredWorldFiles.Begin();
        Assert.True(DeferredWorldFiles.Pending.IsCompleted, "a client without deferred files waits for them");

        var release = new TaskCompletionSource();
        var started = 0;
        DeferredWorldFiles.Defer(async () =>
        {
            started++;
            await release.Task;
        });
        Assert.True(!DeferredWorldFiles.Pending.IsCompleted && started == 0, "the deferred download ran before the field showed");
        DeferredWorldFiles.Begin();
        DeferredWorldFiles.Begin();
        Assert.True(started == 1 && !DeferredWorldFiles.Pending.IsCompleted, "the deferred download did not start once");
        release.SetResult();
        await DeferredWorldFiles.Pending.WaitAsync(TimeSpan.FromSeconds(5));

        DeferredWorldFiles.Defer(() => Task.FromException(new HttpRequestException("offline")));
        DeferredWorldFiles.Begin();
        await DeferredWorldFiles.Pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(DeferredWorldFiles.Pending.IsCompletedSuccessfully, "a failed download left readers waiting or faulted them");
    }
}
