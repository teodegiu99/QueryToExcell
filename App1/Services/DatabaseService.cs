using Microsoft.Extensions.Configuration; // Aggiungi questo in alto!
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using QueryToExcell.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace QueryToExcell.Services // Ricorda di usare il namespace corretto del tuo progetto
{
    public class DatabaseService
    {

        private readonly string _connectionString;
        private readonly string _oracleConnectionString;

        public DatabaseService()
        {
            string cartellaApp = AppDomain.CurrentDomain.BaseDirectory;
            var builder = new ConfigurationBuilder()
                .SetBasePath(cartellaApp)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfiguration config = builder.Build();

            // Legge i dati dalla sezione "ConnectionStrings"
            _connectionString = config.GetConnectionString("PostgresConnection");
            _oracleConnectionString = config.GetConnectionString("OracleConnection");
        }

        private string RimuoviDominio(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "";

            // Se c'è un backslash (es. DOMINIO\mario), prendiamo solo quello che c'è a destra
            if (username.Contains("\\"))
            {
                return username.Split('\\')[1].Trim();
            }

            return username.Trim();
        }

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

        public List<QueryInfo> OttieniTutteLeQuery(string currentUsername, bool isCedUser)
        {
            var lista = new List<QueryInfo>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string sql = "";

                string utentePulito = RimuoviDominio(currentUsername);
                // Se è CED vede TUTTO, altrimenti facciamo una JOIN per vedere solo le sue
                if (isCedUser)
                {
                    sql = "SELECT id, titolo, sql_text FROM estrazioni_query ORDER BY titolo";
                }
                else
                {
                    sql = @"SELECT eq.id, eq.titolo, eq.sql_text 
                    FROM estrazioni_query eq
                    INNER JOIN estrazioni_accessi ea ON eq.id = ea.query_id
                    INNER JOIN utenti_app ua ON ea.utente_id = ua.id
                    WHERE LOWER(ua.username) = LOWER(@currUser) OR LOWER(ua.username) LIKE LOWER('%\\' || @currUser)
                    ORDER BY eq.titolo";
                }

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    if (!isCedUser) cmd.Parameters.AddWithValue("currUser", utentePulito); 

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new QueryInfo
                            {
                                Id = reader.GetInt32(0),
                                Title = reader.GetString(1),
                                SqlText = reader.GetString(2),
                                Parameters = new List<QueryParameter>()
                            });
                        }
                    }
                }

                foreach (var q in lista)
                {
                    string sqlParam = "SELECT nome_parametro, tipo_parametro, label_utente FROM estrazioni_parametri WHERE query_id = @qid";
                    using (var cmdP = new NpgsqlCommand(sqlParam, conn))
                    {
                        cmdP.Parameters.AddWithValue("qid", q.Id);
                        using (var readerP = cmdP.ExecuteReader())
                        {
                            while (readerP.Read())
                            {
                                q.Parameters.Add(new QueryParameter
                                {
                                    Name = readerP.GetString(0),
                                    Type = readerP.GetString(1),
                                    Label = readerP.GetString(2)
                                });
                            }
                        }
                    }
                }
            }
            return lista;
        }
        public DataTable EseguiEstrazioneOracle(string sqlText, Dictionary<string, object> parametriInseriti)
        {
            var dataTable = new DataTable();

            using (var conn = new OracleConnection(_oracleConnectionString))
            {
                conn.Open();

                // PULIZIA ESTREMA DELLA QUERY:
                // 1. Togliamo gli invii/a capo strani di Windows
                // 2. Togliamo eventuali punti e virgola finali sfuggiti al CED
                string queryPulita = sqlText.Replace("\r\n", " ").Replace("\n", " ").Trim().TrimEnd(';');

                using (var cmd = new OracleCommand(queryPulita, conn))
                {
                    cmd.BindByName = true;

                    foreach (var param in parametriInseriti)
                    {
                        string nomePulito = param.Key.Replace(":", "").Trim();

                        // Gestione dei valori Nulli sicura per Oracle
                        object valoreDaPassare = param.Value ?? DBNull.Value;

                        cmd.Parameters.Add(new OracleParameter(nomePulito, valoreDaPassare));
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        dataTable.Load(reader);
                    }
                }
            }
            return dataTable;
        }

        public bool EsisteUtenteApp(string username)
        {
            // Puliamo subito l'input!
            string utentePulito = RimuoviDominio(username);

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                // Cerca 'mario' OPPURE qualsiasi cosa finisca per '\mario'
                string sql = "SELECT COUNT(1) FROM utenti_app WHERE LOWER(username) = LOWER(@usr) OR LOWER(username) LIKE LOWER('%\\' || @usr)";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("usr", utentePulito);
                    long count = (long)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
        }
    


        // Cambia la firma del metodo aggiungendo "List<string> utentiAbilitati"
        public void SalvaNuovaQuery(string titolo, string sqlText, List<QueryParameter> parametri, List<string> utentiAbilitati)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // 1. Inserisce la Query
                string insertQuery = "INSERT INTO estrazioni_query (titolo, sql_text) VALUES (@titolo, @sql) RETURNING id;";
                int nuovaQueryId;
                using (var cmd = new NpgsqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("titolo", titolo);
                    cmd.Parameters.AddWithValue("sql", sqlText);
                    nuovaQueryId = (int)cmd.ExecuteScalar();
                }

                // 2. Inserisce i Parametri
                foreach (var parametro in parametri)
                {
                    string insertParam = @"INSERT INTO estrazioni_parametri (query_id, nome_parametro, tipo_parametro, label_utente) VALUES (@qid, @nome, @tipo, @label);";
                    using (var pCmd = new NpgsqlCommand(insertParam, conn))
                    {
                        pCmd.Parameters.AddWithValue("qid", nuovaQueryId);
                        pCmd.Parameters.AddWithValue("nome", parametro.Name);
                        pCmd.Parameters.AddWithValue("tipo", parametro.Type);
                        pCmd.Parameters.AddWithValue("label", parametro.Label);
                        pCmd.ExecuteNonQuery();
                    }
                }

                // 3. NUOVO: Gestione Utenti e Accessi
                foreach (var username in utentiAbilitati)
                {
                    // Puliamo il nome che il CED ha inserito nel form
                    string utentePulito = RimuoviDominio(username);

                    string queryIdUtente = "SELECT id FROM utenti_app WHERE LOWER(username) = LOWER(@usr) OR LOWER(username) LIKE LOWER('%\\' || @usr)";
                    int utenteId = 0;

                    using (var cmdGetId = new NpgsqlCommand(queryIdUtente, conn))
                    {
                        cmdGetId.Parameters.AddWithValue("usr", utentePulito);
                        var result = cmdGetId.ExecuteScalar();

                        if (result != null)
                        {
                            utenteId = Convert.ToInt32(result);

                            // ... il codice della insert rimane identico ...
                            string insertAccesso = "INSERT INTO estrazioni_accessi (query_id, utente_id) VALUES (@qid, @uid)";
                            using (var cmdAcc = new NpgsqlCommand(insertAccesso, conn))
                            {
                                cmdAcc.Parameters.AddWithValue("qid", nuovaQueryId);
                                cmdAcc.Parameters.AddWithValue("uid", utenteId);
                                cmdAcc.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
        }
        public void EliminaQuery(int queryId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                // Grazie al CASCADE, questo cancellerà in automatico anche le righe in estrazioni_parametri!
                string sql = "DELETE FROM estrazioni_query WHERE id = @id";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("id", queryId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<string> OttieniUtentiPerQuery(int queryId)
        {
            var utenti = new List<string>();
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                string sql = @"SELECT ua.username 
                       FROM estrazioni_accessi ea 
                       INNER JOIN utenti_app ua ON ea.utente_id = ua.id 
                       WHERE ea.query_id = @qid";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("qid", queryId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            utenti.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return utenti;
        }

        // 2. Aggiorna tutto (Query, Parametri e Accessi)
        public void AggiornaQuery(int queryId, string titolo, string sqlText, List<QueryParameter> parametri, List<string> utentiAbilitati)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // A. Aggiorniamo il testo e il titolo
                string updQuery = "UPDATE estrazioni_query SET titolo = @titolo, sql_text = @sql WHERE id = @id;";
                using (var cmd = new NpgsqlCommand(updQuery, conn))
                {
                    cmd.Parameters.AddWithValue("titolo", titolo);
                    cmd.Parameters.AddWithValue("sql", sqlText);
                    cmd.Parameters.AddWithValue("id", queryId);
                    cmd.ExecuteNonQuery();
                }

                // B. Cancelliamo i vecchi parametri e inseriamo quelli nuovi
                using (var cmdDelP = new NpgsqlCommand("DELETE FROM estrazioni_parametri WHERE query_id = @id", conn))
                {
                    cmdDelP.Parameters.AddWithValue("id", queryId);
                    cmdDelP.ExecuteNonQuery();
                }

                foreach (var parametro in parametri)
                {
                    string insertParam = "INSERT INTO estrazioni_parametri (query_id, nome_parametro, tipo_parametro, label_utente) VALUES (@qid, @nome, @tipo, @label);";
                    using (var pCmd = new NpgsqlCommand(insertParam, conn))
                    {
                        pCmd.Parameters.AddWithValue("qid", queryId);
                        pCmd.Parameters.AddWithValue("nome", parametro.Name);
                        pCmd.Parameters.AddWithValue("tipo", parametro.Type);
                        pCmd.Parameters.AddWithValue("label", parametro.Label);
                        pCmd.ExecuteNonQuery();
                    }
                }

                // C. Cancelliamo i vecchi accessi e inseriamo i nuovi (solo utenti validati)
                using (var cmdDelA = new NpgsqlCommand("DELETE FROM estrazioni_accessi WHERE query_id = @id", conn))
                {
                    cmdDelA.Parameters.AddWithValue("id", queryId);
                    cmdDelA.ExecuteNonQuery();
                }

                foreach (var username in utentiAbilitati)
                {
                    string utentePulito = RimuoviDominio(username);
                    string queryIdUtente = "SELECT id FROM utenti_app WHERE LOWER(username) = LOWER(@usr) OR LOWER(username) LIKE LOWER('%\\' || @usr)";
                    int utenteId = 0;

                    using (var cmdGetId = new NpgsqlCommand(queryIdUtente, conn))
                    {
                        cmdGetId.Parameters.AddWithValue("usr", utentePulito);
                        var result = cmdGetId.ExecuteScalar();
                        if (result != null)
                        {
                            utenteId = Convert.ToInt32(result);
                            string insertAccesso = "INSERT INTO estrazioni_accessi (query_id, utente_id) VALUES (@qid, @uid)";
                            using (var cmdAcc = new NpgsqlCommand(insertAccesso, conn))
                            {
                                cmdAcc.Parameters.AddWithValue("qid", queryId);
                                cmdAcc.Parameters.AddWithValue("uid", utenteId);
                                cmdAcc.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
        }



    }
}