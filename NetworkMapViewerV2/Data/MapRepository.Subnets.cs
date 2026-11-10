using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;

namespace NetworkMapViewerV2.Data
{
    // Named CIDR ranges shown in the IPAM sidebar. Stored in the database so every user of the app sees the same list.
    public partial class MapRepository
    {
        private static bool _subnetsTableReady;

        /// <summary>
        /// Creates dbo.Subnets on first use, so existing databases pick it up without a manual migration.
        /// The database account needs CREATE TABLE rights the first time; afterwards read/write is enough.
        /// </summary>
        private static void EnsureSubnetsTable(SqlConnection connection)
        {
            if (_subnetsTableReady) return;

            const string sql = @"
                IF OBJECT_ID('dbo.Subnets', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Subnets
                    (
                        SubnetId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        Name NVARCHAR(200) NOT NULL,
                        Cidr NVARCHAR(50) NOT NULL,
                        CONSTRAINT UQ_Subnets_Cidr UNIQUE (Cidr)
                    );
                END;";

            using var cmd = new SqlCommand(sql, connection);
            cmd.ExecuteNonQuery();
            _subnetsTableReady = true;
        }

        public List<SavedSubnet> GetSubnets()
        {
            var result = new List<SavedSubnet>();
            using var connection = GetOpenConnection();
            EnsureSubnetsTable(connection);

            using var cmd = new SqlCommand("SELECT SubnetId, Name, Cidr FROM dbo.Subnets ORDER BY Name, Cidr", connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new SavedSubnet
                {
                    SubnetId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Cidr = reader.GetString(2)
                });
            }
            return result;
        }

        /// <summary>Inserts (SubnetId = 0) or updates a range and writes an audit log entry. Returns the saved range.</summary>
        public SavedSubnet SaveSubnet(SavedSubnet subnet)
        {
            using var connection = GetOpenConnection();
            EnsureSubnetsTable(connection);
            using var transaction = connection.BeginTransaction();

            try
            {
                if (subnet.SubnetId == 0)
                {
                    using var insert = new SqlCommand(
                        "INSERT INTO dbo.Subnets (Name, Cidr) VALUES (@Name, @Cidr); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        connection, transaction);
                    insert.Parameters.AddWithValue("@Name", subnet.Name);
                    insert.Parameters.AddWithValue("@Cidr", subnet.Cidr);
                    subnet.SubnetId = Convert.ToInt32(insert.ExecuteScalar());

                    InsertAuditLogInternal(actionType: "INSERT", target: "Subnets", details: $"Saved IP range: {subnet.Name} ({subnet.Cidr})",
                        targetId: subnet.SubnetId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: transaction);
                }
                else
                {
                    string oldName = "", oldCidr = "";
                    using (var select = new SqlCommand("SELECT Name, Cidr FROM dbo.Subnets WHERE SubnetId = @Id", connection, transaction))
                    {
                        select.Parameters.AddWithValue("@Id", subnet.SubnetId);
                        using var reader = select.ExecuteReader();
                        if (!reader.Read()) throw new InvalidOperationException("This range no longer exists. It was probably deleted by someone else.");
                        oldName = reader.GetString(0);
                        oldCidr = reader.GetString(1);
                    }

                    using var update = new SqlCommand("UPDATE dbo.Subnets SET Name = @Name, Cidr = @Cidr WHERE SubnetId = @Id", connection, transaction);
                    update.Parameters.AddWithValue("@Id", subnet.SubnetId);
                    update.Parameters.AddWithValue("@Name", subnet.Name);
                    update.Parameters.AddWithValue("@Cidr", subnet.Cidr);
                    update.ExecuteNonQuery();

                    InsertAuditLogInternal(actionType: "UPDATE", target: "Subnets",
                        details: $"IP range changed: {oldName} ({oldCidr}) -> {subnet.Name} ({subnet.Cidr})",
                        targetId: subnet.SubnetId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: transaction);
                }

                transaction.Commit();
                return subnet;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Unique index / constraint violation on Cidr.
                transaction.Rollback();
                throw new InvalidOperationException("That CIDR range is already saved.", ex);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void DeleteSubnet(int subnetId)
        {
            using var connection = GetOpenConnection();
            EnsureSubnetsTable(connection);
            using var transaction = connection.BeginTransaction();

            try
            {
                string name = "", cidr = "";
                using (var select = new SqlCommand("SELECT Name, Cidr FROM dbo.Subnets WHERE SubnetId = @Id", connection, transaction))
                {
                    select.Parameters.AddWithValue("@Id", subnetId);
                    using var reader = select.ExecuteReader();
                    if (!reader.Read()) { transaction.Rollback(); return; } // already gone
                    name = reader.GetString(0);
                    cidr = reader.GetString(1);
                }

                using var delete = new SqlCommand("DELETE FROM dbo.Subnets WHERE SubnetId = @Id", connection, transaction);
                delete.Parameters.AddWithValue("@Id", subnetId);
                delete.ExecuteNonQuery();

                InsertAuditLogInternal(actionType: "DELETE", target: "Subnets", details: $"Removed IP range: {name} ({cidr})",
                    targetId: subnetId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: transaction);

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
