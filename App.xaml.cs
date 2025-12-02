using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace blender_selecter
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage());
        }
    }
}