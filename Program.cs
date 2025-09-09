namespace CrazyArcadeServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var server = new AsyncServer();
            server.Start(5000);

            Console.ReadLine();
        }
    }
}
