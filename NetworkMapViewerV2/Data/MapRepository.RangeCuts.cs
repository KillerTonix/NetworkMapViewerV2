using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;

namespace NetworkMapViewerV2.Data
{
    // The planned address blocks of a branch network (Printers .10-.30 and so on), edited in the Range Cut window.
    // One list for the whole company, stored in the database so every user of the app sees the same blocks.
    public partial class MapRepository
    {
        private static bool _rangeCutsTableReady;

        /// <summary>
        /// Creates dbo.RangeCuts on first use and fills it with the default blocks. The database account needs
        /// CREATE TABLE rights the first time; afterwards read/write is enough.
        /// </summary>
        private static void EnsureRangeCutsTable(SqlConnection connection)
        {
            if (_rangeCutsTableReady) return;

            const string sql = @"
                IF OBJECT_ID('dbo.RangeCuts', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.RangeCuts
                    (
                        RangeCutId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        Label NVARCHAR(100) NOT NULL,
                        FromOctet INT NOT NULL,
                        ToOctet INT NOT NULL,
                        SortOrder INT NOT NULL
                    );

                    INSERT INTO dbo.RangeCuts (Label, FromOctet, ToOctet, SortOrder)
                    VALUES ('Printers', 10, 30, 1), ('Phones', 50, 70, 2), ('PCs / NUCs', 101, 140, 3);
                END;";

            using var cmd = new SqlCommand(sql, connection);
            cmd.ExecuteNonQuery();
            _rangeCutsTableReady = true;
        }

        public List<RangeCut> GetRangeCuts()
        {
            using var connection = GetOpenConnection();
            EnsureRangeCutsTable(connection);
            return ReadRangeCutsInternal(connection, null);
        }

        private static List<RangeCut> ReadRangeCutsInternal(SqlConnection connection, SqlTransaction? transaction)
        {
            var result = new List<RangeCut>();
            using var cmd = new SqlCommand("SELECT RangeCutId, Label, FromOctet, ToOctet FROM dbo.RangeCuts ORDER BY SortOrder, FromOctet", connection, transaction);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new RangeCut
                {
                    RangeCutId = reader.GetInt32(0),
                    Label = reader.GetString(1),
                    From = reader.GetInt32(2),
                    To = reader.GetInt32(3)
                });
            }
            return result;
        }

        /// <summary>Replaces the whole list in one transaction and writes an audit log entry.</summary>
        public void SaveRangeCuts(IReadOnlyList<RangeCut> ranges)
        {
            string? problem = IpamHelper.ValidateCutRanges(ranges);
            if (problem != null) throw new InvalidOperationException(problem);

            using var connection = GetOpenConnection();
            EnsureRangeCutsTable(connection);
            using var transaction = connection.BeginTransaction();

            try
            {
                string before = IpamHelper.DescribeCutRanges(ReadRangeCutsInternal(connection, transaction));

                using (var delete = new SqlCommand("DELETE FROM dbo.RangeCuts", connection, transaction))
                {
                    delete.ExecuteNonQuery();
                }

                var ordered = ranges.OrderBy(r => r.From).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    using var insert = new SqlCommand(
                        "INSERT INTO dbo.RangeCuts (Label, FromOctet, ToOctet, SortOrder) VALUES (@Label, @From, @To, @Sort)",
                        connection, transaction);
                    insert.Parameters.AddWithValue("@Label", ordered[i].Label.Trim());
                    insert.Parameters.AddWithValue("@From", ordered[i].From);
                    insert.Parameters.AddWithValue("@To", ordered[i].To);
                    insert.Parameters.AddWithValue("@Sort", i + 1);
                    insert.ExecuteNonQuery();
                }

                string after = IpamHelper.DescribeCutRanges(ordered);
                InsertAuditLogInternal(actionType: "UPDATE", target: "RangeCuts",
                    details: $"Cut ranges changed: {before} -> {after}",
                    targetId: 0, mapName: null, oldValue: before, newValue: after,
                    connection: connection, transaction: transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
