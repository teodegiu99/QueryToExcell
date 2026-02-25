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

        public List<QueryInfo> OttieniTutteLeQuery()
        {
            var lista = new List<QueryInfo>();

            using (var conn = new NpgsqlConnection(_connectionString)) // Usa la stringa Postgres
            {
                conn.Open();
                string sql = "SELECT id, titolo, sql_text FROM estrazioni_query ORDER BY titolo";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var query = new QueryInfo
                        {
                            Id = reader.GetInt32(0),
                            Title = reader.GetString(1),
                            SqlText = reader.GetString(2),
                            Parameters = new List<QueryParameter>()
                        };
                        lista.Add(query);
                    }
                }

                // Ora carichiamo i parametri per ogni query
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
                                    Name = readerP.GetString(0), // Es. "DataInizio" (senza i due punti)
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

            using (var conn = new OracleConnection(_oracleConnectionString)) // Usa la stringa Oracle
            {
                conn.Open();
                using (var cmd = new OracleCommand(sqlText, conn))
                {
                    // FONDAMENTALE IN ORACLE: Mappa i parametri per nome e non per posizione!
                    cmd.BindByName = true;

                    // Aggiungiamo i parametri che l'utente ha compilato nella modale
                    foreach (var param in parametriInseriti)
                    {
                        // Rimuoviamo i due punti ":" se per caso il CED li ha salvati nel nome su Postgres
                        string nomePulito = param.Key.Replace(":", "");
                        cmd.Parameters.Add(new OracleParameter(nomePulito, param.Value));
                    }

                    // Eseguiamo la query e carichiamo i risultati in una DataTable
                    using (var reader = cmd.ExecuteReader())
                    {
                        dataTable.Load(reader);
                    }
                }
            }
            return dataTable;
        }


        public void SalvaNuovaQuery(string titolo, string sqlText, List<QueryParameter> parametri)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                // 1. Inseriamo la query e ci facciamo restituire l'ID appena creato (RETURNING id)
                string insertQuery = "INSERT INTO estrazioni_query (titolo, sql_text) VALUES (@titolo, @sql) RETURNING id;";

                int nuovaQueryId;
                using (var cmd = new NpgsqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("titolo", titolo);
                    cmd.Parameters.AddWithValue("sql", sqlText);

                    // ExecuteScalar esegue la query e ci restituisce l'ID generato
                    nuovaQueryId = (int)cmd.ExecuteScalar();
                }

                // 2. Salviamo tutti i parametri associati a questo ID
                foreach (var parametro in parametri)
                {
                    string insertParam = @"INSERT INTO estrazioni_parametri 
                                 (query_id, nome_parametro, tipo_parametro, label_utente) 
                                 VALUES (@qid, @nome, @tipo, @label);";

                    using (var pCmd = new NpgsqlCommand(insertParam, conn))
                    {
                        pCmd.Parameters.AddWithValue("qid", nuovaQueryId);
                        pCmd.Parameters.AddWithValue("nome", parametro.Name);
                        pCmd.Parameters.AddWithValue("tipo", parametro.Type);
                        pCmd.Parameters.AddWithValue("label", parametro.Label);

                        pCmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}