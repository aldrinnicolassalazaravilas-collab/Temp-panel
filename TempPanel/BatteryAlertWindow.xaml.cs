using System.Windows;

namespace TempPanel;

/// <summary>
/// Ventana emergente de alerta de batería. El botón OK la cierra de inmediato.
/// En modo prueba muestra el prefijo [PRUEBA] y no afirma un nivel real.
/// </summary>
public partial class BatteryAlertWindow : Window
{
    public const string RealMessage =
        "La batería de tu portátil ha llegado al 80 %. Si no necesitas toda la autonomía, " +
        "puedes desconectar el cargador para ayudar a reducir el desgaste de la batería.";

    public BatteryAlertWindow(bool isTest = false)
    {
        InitializeComponent();
        if (isTest)
        {
            Title = "TempPanel — Prueba de notificación";
            TitleText.Text = "TempPanel — Prueba de notificación";
            BodyText.Text = "[PRUEBA] Así se verá el aviso. " + RealMessage;
        }
        else
        {
            BodyText.Text = RealMessage;
        }
        OkButton.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close(); // cierre inmediato
    }
}
