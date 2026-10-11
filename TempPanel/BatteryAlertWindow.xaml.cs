using System.Windows;

namespace TempPanel;

/// <summary>
/// Ventana emergente de avisos de batería. El botón OK la cierra de inmediato.
/// Los textos los construye el llamador (umbral configurado o carga completa);
/// en modo prueba se añade el prefijo [PRUEBA] y no afirma un nivel real.
/// </summary>
public partial class BatteryAlertWindow : Window
{
    public BatteryAlertWindow(string title, string message, bool isTest = false)
    {
        InitializeComponent();
        if (isTest)
        {
            Title = "TempPanel — Prueba de notificación";
            TitleText.Text = "TempPanel — Prueba de notificación";
            BodyText.Text = "[PRUEBA] Así se verá el aviso. " + message;
        }
        else
        {
            Title = title;
            TitleText.Text = title;
            BodyText.Text = message;
        }
        OkButton.Focus();
    }

    public static string ThresholdMessage(double thresholdPct) =>
        $"La batería de tu portátil ha llegado al {thresholdPct:0} %. Si no necesitas toda la autonomía, " +
        "puedes desconectar el cargador para ayudar a reducir el desgaste de la batería.";

    public static string FullMessage(double pct) =>
        $"La batería está al {pct:0} %. Puedes desconectar el cargador si no la necesitas al máximo.";

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close(); // cierre inmediato
    }
}
