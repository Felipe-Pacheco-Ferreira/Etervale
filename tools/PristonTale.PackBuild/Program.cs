using PristonTale.PackBuild;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: ptpack <assets> <world-database.db> <output-dir>  (see docs/packs.md)");
    return 2;
}

return PackBuilder.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
