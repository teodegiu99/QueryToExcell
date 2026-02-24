using Npgsql;
using System;

namespace QueryToExcell.Services // Ricorda di usare il namespace corretto del tuo progetto
{
    public class DatabaseService
    {
        // ATTENZIONE: Inserisci qui i dati reali del tuo database PostgreSQL!
        private readonly string _connectionString = "Host=SZBLBTKTDB01;Port=5432;Database=tktdbtest;Username=szblbtktdb01;Password=Z20250101b!";

        public bool CheckIfUserIsIT(string windowsUser)
        {
            try
            {
                // Spesso l'utente di Windows arriva come "DOMINIO\nomeutente".
                // Se nel database salvate solo "nomeutente", dobbiamo tagliare via il dominio:
                string usernameAd = windowsUser;
                if (windowsUser.Contains("\\"))
                {
                    usernameAd = windowsUser.Split('\\')[1];
                }

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    // Query per contare se l'utente esiste nella tabella utenti_it
                    string sql = "SELECT COUNT(1) FROM utenti_it WHERE LOWER(username_ad) = LOWER(@user)";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("user", windowsUser);

                        long count = (long)cmd.ExecuteScalar();

                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // In caso di errore di connessione (es. DB spento o credenziali errate)
                System.Diagnostics.Debug.WriteLine($"Errore DB: {ex.Message}");
                return false;
            }
        }
    }
}