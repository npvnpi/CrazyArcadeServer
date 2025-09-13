namespace CrazyArcadeServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var server = new CrazyArcadeServer();
            server.Start(5000);
            Console.ReadLine();
        }
    }
}
