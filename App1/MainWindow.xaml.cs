using QueryToExcell.Services; // Assicurati che il nome sia corretto
using Microsoft.UI.Xaml;
using System;
using System.Security.Principal;

namespace QueryToExcell
{
    public sealed partial class MainWindow : Window
    {
        public string CurrentWindowsUser { get; set; }

        public MainWindow()
        {
            this.InitializeComponent();

            CurrentWindowsUser = WindowsIdentity.GetCurrent().Name;

            // NON TAGLIAMO PIÙ NIENTE! Passiamo l'utente intero con tutto il dominio.
            string utenteCompleto = CurrentWindowsUser;

            TxtDebugInfo.Text = $"Sto cercando l'utente completo '{utenteCompleto}' nel database...";

            VerificaSeUtenteCED(utenteCompleto);
        }

        private void VerificaSeUtenteCED(string usernameDaCercare)
        {
            try
            {
                var dbService = new DatabaseService();

                // Chiamiamo il database
                bool isCedUser = dbService.CheckIfUserIsIT(usernameDaCercare);

                if (isCedUser)
                {
                    TxtDebugInfo.Text = "Autenticazione riuscita: Sei nel gruppo IT!";
                    TxtDebugInfo.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);

                    // Mostriamo il bottone MANUALMENTE (metodo sicuro)
                    BtnAddQuery.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtDebugInfo.Text = $"Accesso base. L'utente '{usernameDaCercare}' non è nella tabella utenti_it.";
                    TxtDebugInfo.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
            }
            catch (Exception ex)
            {
                // Se la connessione fallisce, lo vediamo a schermo!
                TxtDebugInfo.Text = $"ERRORE DATABASE: {ex.Message}";
                TxtDebugInfo.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
        }
    }
}