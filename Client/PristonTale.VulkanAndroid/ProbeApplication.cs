using System;
using System.Runtime.InteropServices;
using System.Threading;
using Android.App;
using Android.Runtime;
using Android.Util;



[Application]
public sealed class ProbeApplication : Application
{
    private const string Tag = "EtervaleVulkanProbe";
    private static readonly MainCallback RunProbe = Run;
    private static PristonTale.Client.Net.GameServerConnection pendingConnection;
    private static string startupFailure;
    private static string animatedLoginEndpoint;
    private static volatile bool firstFrameReady;

    internal static bool FirstFrameReady => firstFrameReady;
    internal static void MarkFirstFrame() => firstFrameReady = true;

    internal static VulkanWorldSession CurrentWorld { get; private set; }

    internal static void SetConnection(PristonTale.Client.Net.GameServerConnection connection, string endpoint = null)
    {
        animatedLoginEndpoint = endpoint;
        Interlocked.Exchange(ref pendingConnection, connection);
    }

    internal static string TakeStartupFailure() => Interlocked.Exchange(ref startupFailure, null);

    internal static void SetWorld(VulkanWorldSession world) => CurrentWorld = world;

    public ProbeApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        SetManagedMain(RunProbe);
        RegisterActivityLifecycleCallbacks(new VulkanHudLifecycle());
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MainCallback();

    [DllImport("main", EntryPoint = "etervale_set_managed_main", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetManagedMain(MainCallback callback);

    private static int Run()
    {
        var connection = Interlocked.Exchange(ref pendingConnection, null);
        if (connection is null)
        {
            return 1;
        }
        try
        {
            using var game = new RicartenProbe(connection, animatedLoginEndpoint);
            try
            {
                game.RunWithVulkanLoader();
                return 0;
            }
            finally
            {
                connection = game.Connection;
            }
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref startupFailure, exception.GetBaseException().Message);
            Log.Error(Tag, exception.ToString());
            return 1;
        }
        finally
        {
            try
            {
                var world = CurrentWorld;
                CurrentWorld = null;
                if (world is not null)
                {
                    world.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                Log.Warn(Tag, exception.Message);
            }
        }
    }
}
