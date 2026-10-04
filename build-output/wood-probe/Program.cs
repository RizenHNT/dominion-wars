namespace WoodPoolProbe
{
    // Phase execution and policy selection live exclusively in PlCsim.SingleMatch.
    internal static class Program
    {
        private static int Main(string[] args) => ProbeRunner.Run(args);
    }
}
