using System.Windows;

namespace TempPanel;

/// <summary>
/// Ventana emergente de alerta de batería. El botón OK la cierra de inmediato.
/// El mensaje usa el umbral configurado (nada hardcodeado al 80 %).
/// En modo prueba muestra el prefijo [PRUEBA] y no afirma un nivel real.
/// </summary>
public partial class BatteryAlertWindow : Window
{
    public BatteryAlertWindow(double thresholdPct, bool isTest = false)
    {
        InitializeComponent();
        string th = thresholdPct.ToString("0");
        string msg = $"La batería de tu portátil ha llegado al {th} %. Si no necesitas toda la autonomía, " +
            "puedes desconectar el cargador para ayudar a reducir el desgaste de la batería.";
        if (isTest)
        {
            Title = "TempPanel — Prueba de notificación";
            TitleText.Text = "TempPanel — Prueba de notificación";
            BodyText.Text = "[PRUEBA] Así se verá el aviso. " + msg;
        }
        else
        {
            Title = $"TempPanel — Batería al {th} %";
            TitleText.Text = $"TempPanel — Batería al {th} %";
            BodyText.Text = msg;
        }
        OkButton.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close(); // cierre inmediato
    }
}
