namespace blender_selecter;

public partial class App : Application
{
    public App(string[] args)
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage());
    }
}