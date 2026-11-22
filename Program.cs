namespace Kontur;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test")
            return SelfTest.Run(args[1]);

        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--screen-test")
            return SelfTest.CheckScreen(args[1]);
        if (args.Length == 2 && args[0] == "--ui-test")
            return SelfTest.CheckWindow(args[1]);

        using var instance = new Mutex(true, @"Local\KonturSK0127", out var first);
        if (!first)
        {
            MessageBox.Show("Окно «Контур» уже открыто.", "Контур");
            return 0;
        }

        var dataFolder = args.Length == 2 && args[0] == "--data-dir"
            ? Path.GetFullPath(args[1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KonturSK0127");
        var files = new LocalFiles(dataFolder);
        using var window = new MainWindow(files, startInHazard: args is [WindowsStartup.LaunchArgument]);
        Application.Run(window);
        return 0;
    }
}
