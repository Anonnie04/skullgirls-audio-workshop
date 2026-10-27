namespace SkullgirlsAudioWorkshop;
internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool smoke = args.FirstOrDefault() == "--smoke-test";
        using var form = new Workshop(smoke ? null : args.FirstOrDefault());
        using var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        if (smoke) { form.Shown += (_, _) => timer.Start(); timer.Tick += (_, _) => { timer.Stop(); form.Close(); }; }
        Application.Run(form);
    }
}
