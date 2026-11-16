using PristonTale.PackBuild;

if (args is ["--summon-models", var assets, var database, var output])
{
    return PackBuilder.RunSummonModels(Path.GetFullPath(assets), Path.GetFullPath(database), Path.GetFullPath(output));
}

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: ptpack <assets> <world-database.db> <output-dir>  (see docs/packs.md)");
    Console.Error.WriteLine("       ptpack --summon-models <assets> <world-database.db> <vulkan-archive-dir>");
    return 2;
}

return PackBuilder.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
