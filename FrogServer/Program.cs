namespace FrogServer;

internal static class Program
{
    private static int Main(string[] args)
    {
        ushort port = 27015;
        int maxRooms = 1024;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && ushort.TryParse(args[++i], out var p): port = p; break;
                case "--max-rooms" when i + 1 < args.Length && int.TryParse(args[++i], out var m): maxRooms = m; break;
                case "--help":
                case "-h":
                    Console.WriteLine("FrogServer --port 27015 --max-rooms 1024");
                    return 0;
            }
        }

        var server = new RelayServer(port, maxRooms);
        server.Run();
        return 0;
    }
}
