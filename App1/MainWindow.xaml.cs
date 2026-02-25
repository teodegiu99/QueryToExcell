using ClosedXML.Excel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryToExcell.Models;
using QueryToExcell.Services; // Assicurati che il nome sia corretto
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Data; // <--- RISOLVE L'ERRORE DATATABLE
using Windows.Storage.Pickers;
using System.Threading.Tasks;


namespace QueryToExcell
{
    public sealed partial class MainWindow : Window
    {
        public string CurrentWindowsUser { get; set; }
        public bool IsCedUser { get; set; }

        public MainWindow()
        {
            this.InitializeComponent();

            CurrentWindowsUser = WindowsIdentity.GetCurrent().Name;

            // NON TAGLIAMO PIÙ NIENTE! Passiamo l'utente intero con tutto il dominio.
            string utenteCompleto = CurrentWindowsUser;


            VerificaSeUtenteCED(utenteCompleto);
            CaricaListaQuery();
        }
        private void VerificaSeUtenteCED(string usernameDaCercare)
        {
            var dbService = new DatabaseService();
            if (dbService.CheckIfUserIsIT(usernameDaCercare))
            {
                // Se sei del CED, ti fa vedere il bottone magico in alto a destra!
                BtnAddQuery.Visibility = Visibility.Visible;
                IsCedUser = true; // Salviamo questa info per usarla dopo quando mostriamo la lista
            }
        }

        private async void BtnAddQuery_Click(object sender, RoutedEventArgs e)
        {
            // Pulisce i campi se erano stati riempiti precedentemente
            TxtTitolo.Text = "";
            TxtSql.Text = "";
            UsersListPanel.Children.Clear(); // <-- AGGIUNGI QUESTO
            TxtNuovoUtente.Text = "";
            ParametersListPanel.Children.Clear(); // Svuota i parametri

            DialogNuovaQuery.XamlRoot = this.Content.XamlRoot;
            await DialogNuovaQuery.ShowAsync();
        }

        // 2. Aggiunge una riga dinamicamente per inserire Nome, Tipo e Label del parametro
        private void BtnAddParameterRow_Click(object sender, RoutedEventArgs e)
        {
            // Creiamo una riga (StackPanel orizzontale)
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 5, 0, 5) };

            var txtName = new TextBox { PlaceholderText = "es. @DataInizio", Width = 150 };

            var cmbType = new ComboBox { Width = 120, SelectedIndex = 0 };
            cmbType.Items.Add("text");
            cmbType.Items.Add("number");
            cmbType.Items.Add("date");

            var txtLabel = new TextBox { PlaceholderText = "Testo per l'utente (es. Data inizio)", Width = 200 };

            var btnRemove = new Button { Content = "❌" };

            // Se l'utente clicca la X, rimuoviamo la riga
            btnRemove.Click += (s, args) => ParametersListPanel.Children.Remove(row);

            // Aggiungiamo i controlli alla riga
            row.Children.Add(txtName);
            row.Children.Add(cmbType);
            row.Children.Add(txtLabel);
            row.Children.Add(btnRemove);

            // Aggiungiamo la riga al pannello nella Modale
            ParametersListPanel.Children.Add(row);
        }

        // 3. Salva tutto nel database quando si clicca "Salva Estrazione"
        private void DialogNuovaQuery_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            try
            {
                var parametri = new List<QueryParameter>();

                // Leggiamo tutte le righe dei parametri che abbiamo aggiunto
                foreach (StackPanel row in ParametersListPanel.Children)
                {
                    var txtName = (TextBox)row.Children[0];
                    var cmbType = (ComboBox)row.Children[1];
                    var txtLabel = (TextBox)row.Children[2];

                    if (!string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        parametri.Add(new QueryParameter
                        {
                            Name = txtName.Text,
                            Type = cmbType.SelectedItem.ToString(),
                            Label = txtLabel.Text
                        });
                    }
                }
                var utentiAbilitati = new List<string>();
                foreach (StackPanel row in UsersListPanel.Children)
                {
                    var txtUsername = (TextBox)row.Children[0];
                    utentiAbilitati.Add(txtUsername.Text);
                }

                // Salviamo su DB!
                var dbService = new DatabaseService();
                dbService.SalvaNuovaQuery(TxtTitolo.Text, TxtSql.Text, parametri, utentiAbilitati);

                // Mostriamo un avviso rapido (facoltativo)
                TxtUserInfo.Text = $"Utente: {CurrentWindowsUser} - ✅ Query '{TxtTitolo.Text}' salvata con successo!";
                CaricaListaQuery();
            }
            catch (Exception ex)
            {
                // In caso di errore SQL, impediamo alla modale di chiudersi e mostriamo l'errore
                args.Cancel = true;
                TxtSql.Text = $"ERRORE SALVATAGGIO: {ex.Message}\n\n{TxtSql.Text}";
            }
        }

        private void CaricaListaQuery()
        {
            var dbService = new DatabaseService();
            // Passiamo chi siamo al database!
            var listaDati = dbService.OttieniTutteLeQuery(CurrentWindowsUser, IsCedUser);

            foreach (var query in listaDati)
            {
                query.PulsanteEliminaVisibile = this.IsCedUser ? Visibility.Visible : Visibility.Collapsed;
            }

            ListaEstrazioni.ItemsSource = listaDati;
        }

        private async void BtnAddUtenteRow_Click(object sender, RoutedEventArgs e)
        {
            string usernameInserito = TxtNuovoUtente.Text.Trim();

            if (string.IsNullOrWhiteSpace(usernameInserito)) return;

            var dbService = new DatabaseService();

            // 1. CONTROLLO DATABASE: L'utente esiste davvero?
            if (!dbService.EsisteUtenteApp(usernameInserito))
            {
                var errDialog = new ContentDialog
                {
                    Title = "Utente non trovato",
                    Content = $"L'utente '{usernameInserito}' non è presente nella tabella di sistema (utenti_app).\nVerifica che sia scritto correttamente.",
                    CloseButtonText = "Ok",
                    XamlRoot = this.Content.XamlRoot
                };
                await errDialog.ShowAsync();
                return; // Blocca tutto, non aggiunge la riga!
            }

            // 2. CONTROLLO DOPPIONI: Evitiamo che il CED inserisca due volte lo stesso nome nella modale
            foreach (StackPanel existingRow in UsersListPanel.Children)
            {
                var txt = (TextBox)existingRow.Children[0];
                if (txt.Text.Equals(usernameInserito, StringComparison.OrdinalIgnoreCase))
                {
                    TxtNuovoUtente.Text = ""; // Pulisce e ignora
                    return;
                }
            }

            // 3. TUTTO OK: Creiamo la riga
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 5, 0, 5) };

            var txtUsername = new TextBox { Text = usernameInserito, IsReadOnly = true, Width = 300 };
            var btnRemove = new Button { Content = "❌" };

            btnRemove.Click += (s, args) => UsersListPanel.Children.Remove(row);

            row.Children.Add(txtUsername);
            row.Children.Add(btnRemove);

            UsersListPanel.Children.Add(row);

            TxtNuovoUtente.Text = ""; // Pulisce il campo di inserimento
        }

        // Quando un utente clicca su una query dalla lista:
        private async void ListaEstrazioni_ItemClick(object sender, ItemClickEventArgs e)
        {
            var querySelezionata = (QueryInfo)e.ClickedItem;

            // Creiamo il form dinamicamente in base ai parametri richiesti
            var formPanel = new StackPanel { Spacing = 15 };
            var dizionarioControlli = new Dictionary<string, FrameworkElement>();

            foreach (var param in querySelezionata.Parameters)
            {
                formPanel.Children.Add(new TextBlock { Text = param.Label, FontWeight = Microsoft.UI.Text.FontWeights.Bold });

                FrameworkElement inputControl;

                if (param.Type == "date")
                {
                    inputControl = new CalendarDatePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
                }
                else if (param.Type == "number")
                {
                    inputControl = new NumberBox { SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
                }
                else
                {
                    inputControl = new TextBox();
                }

                formPanel.Children.Add(inputControl);
                dizionarioControlli.Add(param.Name, inputControl);
            }

            var dialog = new ContentDialog
            {
                Title = $"Esecuzione: {querySelezionata.Title}",
                Content = formPanel,
                PrimaryButtonText = "Esegui e Scarica Excel",
                CloseButtonText = "Annulla",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                GeneraExcel(querySelezionata, dizionarioControlli);
            }
        }

        // Il motore che estrae ed esporta:
        private async void GeneraExcel(QueryInfo query, Dictionary<string, FrameworkElement> controlli)
        {
            try
            {
                // 1. Leggiamo i valori inseriti dall'utente ORA, prima di andare in background
                var parametriPerOracle = new Dictionary<string, object>();

                foreach (var ctrl in controlli)
                {
                    string nomeParametro = ctrl.Key;
                    object valore = null;

                    if (ctrl.Value is CalendarDatePicker datePicker && datePicker.Date.HasValue)
                        valore = datePicker.Date.Value.DateTime;
                    else if (ctrl.Value is NumberBox numberBox && !double.IsNaN(numberBox.Value))
                        valore = numberBox.Value;
                    else if (ctrl.Value is TextBox textBox)
                        valore = textBox.Text;

                    parametriPerOracle.Add(nomeParametro, valore);
                }

                // =======================================================
                // 2. ACCENDIAMO LA SCHERMATA DI CARICAMENTO!
                LoadingOverlay.Visibility = Visibility.Visible;
                // =======================================================

                // 3. Spostiamo il lavoro pesante su un thread separato (Background) 
                // in modo da far girare fluida la rotellina
                byte[] excelBytes = await Task.Run(() =>
                {
                    // A. Interroga Oracle
                    var dbService = new DatabaseService();
                    DataTable datiEstratti = dbService.EseguiEstrazioneOracle(query.SqlText, parametriPerOracle);

                    // B. Genera Excel (ClosedXML)
                    using var workbook = new XLWorkbook();
                    var worksheet = workbook.Worksheets.Add("Estrazione");
                    worksheet.Cell(1, 1).InsertTable(datiEstratti);
                    worksheet.Columns().AdjustToContents();

                    // C. Salva l'Excel in memoria e lo restituisce alla nostra App principale
                    using var memoryStream = new MemoryStream();
                    workbook.SaveAs(memoryStream);
                    return memoryStream.ToArray();
                });

                // =======================================================
                // 4. LAVORO FINITO! Spegniamo il caricamento per far scegliere la cartella
                LoadingOverlay.Visibility = Visibility.Collapsed;
                // =======================================================

                // 5. Chiediamo all'utente dove salvare il file
                var savePicker = new FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
                savePicker.FileTypeChoices.Add("File Excel", new List<string>() { ".xlsx" });

                // Puliamo il titolo da eventuali spazi o caratteri strani
                string titoloPulito = string.Join("_", query.Title.Split(Path.GetInvalidFileNameChars()));
                savePicker.SuggestedFileName = $"Estrazione_{titoloPulito}_{DateTime.Now:yyyyMMdd}.xlsx";

                var file = await savePicker.PickSaveFileAsync();

                if (file != null)
                {
                    // Salviamo fisicamente il file sul PC dell'utente
                    await Windows.Storage.FileIO.WriteBytesAsync(file, excelBytes);
                    TxtUserInfo.Text = $"✅ Excel '{query.Title}' salvato correttamente!";
                }
            }
            catch (Exception ex)
            {
                // Se c'è un errore (es. query sbagliata), spegniamo la rotellina prima di dare l'errore!
                LoadingOverlay.Visibility = Visibility.Collapsed;

                var errDialog = new ContentDialog
                {
                    Title = "Errore durante l'estrazione",
                    Content = ex.Message,
                    CloseButtonText = "Ok",
                    XamlRoot = this.Content.XamlRoot
                };
                await errDialog.ShowAsync();
            }
            finally
            {
                // Sicurezza extra: spegne la rotellina in qualsiasi caso (anche errori imprevisti)
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnEliminaQuery_Click(object sender, RoutedEventArgs e)
        {
            // Capiamo quale bottone è stato premuto e recuperiamo la query nascosta nel "Tag"
            var button = (Button)sender;
            var queryDaEliminare = (QueryInfo)button.Tag;

            if (queryDaEliminare == null) return;

            // Chiediamo conferma prima di fare danni irreparabili!
            var dialog = new ContentDialog
            {
                Title = "Conferma Eliminazione",
                Content = $"Sei sicuro di voler eliminare definitivamente l'estrazione '{queryDaEliminare.Title}'?\nL'operazione è irreversibile e cancellerà anche tutte le variabili associate.",
                PrimaryButtonText = "Sì, Elimina",
                CloseButtonText = "Annulla",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    var dbService = new DatabaseService();
                    dbService.EliminaQuery(queryDaEliminare.Id);

                    // Avviso di successo e ricaricamento della lista
                    TxtUserInfo.Text = $"Utente: {CurrentWindowsUser} - 🗑️ Estrazione '{queryDaEliminare.Title}' eliminata!";
                    CaricaListaQuery();
                }
                catch (Exception ex)
                {
                    TxtUserInfo.Text = $"ERRORE ELIMINAZIONE: {ex.Message}";
                }
            }
        }
    }
}
