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
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    // TRIM() rimuove gli spazi invisibili
                    // LOWER() ignora maiuscole e minuscole
                    string sql = "SELECT COUNT(1) FROM it_utenti WHERE TRIM(LOWER(username_ad)) = TRIM(LOWER(@user))";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        // Passiamo l'utente intero (es. DOMINIO\mdegi) esattamente come arriva da Windows
                        cmd.Parameters.AddWithValue("user", windowsUser);

                        long count = (long)cmd.ExecuteScalar();

                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // Se c'è un errore lo rilanciamo così lo vediamo nella modale
                throw new Exception(ex.Message);
            }
        }
    }
}