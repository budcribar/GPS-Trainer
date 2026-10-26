namespace GpsTrainer.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		// Pan leaves the window full height and only shifts a focused field. On a short
		// screen the Login button stays under the keyboard, so a tap types into the field.
		Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
			this,
			Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new NavigationPage(new LoginPage()));
	}
}
