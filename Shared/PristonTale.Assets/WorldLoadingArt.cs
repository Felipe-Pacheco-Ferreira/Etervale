namespace PristonTale.Assets;

// The original full-screen pictures shown while the world loads: every picture of this directory under the asset
// root. The Android client shows one of them, chosen at random, between the character selection and the world; its
// build packages them as PNG under the same names.
public static class WorldLoadingArt
{
    public const string Directory = "StartImage/login/Loading";
    public const string Extension = ".bmp";

    // The height the Android build packages a picture at, at most.
    public const int MostPackagedHeight = 720;
}
