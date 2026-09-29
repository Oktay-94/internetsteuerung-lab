using Internetsteuerung.Core;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Internetsteuerung.Infrastructure;

/// <summary>Opens connections; all queries are parameterised, as in the original Database class.</summary>
public sealed class Verbindungsfabrik(IOptions<DatenbankOptions> optionen)
{
    public async Task<MySqlConnection> OeffneAsync(CancellationToken ct)
    {
        var verbindung = new MySqlConnection(optionen.Value.ConnectionString);
        await verbindung.OpenAsync(ct).ConfigureAwait(false);
        return verbindung;
    }

    /// <summary>SELECT 1, the start-up check TestConnectionAsync of the original.</summary>
    public async Task<bool> TesteVerbindungAsync(CancellationToken ct = default)
    {
        try
        {
            await using var verbindung = await OeffneAsync(ct).ConfigureAwait(false);
            await using var befehl = new MySqlCommand("SELECT 1", verbindung);
            return Convert.ToInt32(await befehl.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        catch (MySqlException)
        {
            return false;
        }
    }
}

public sealed class MariaDbBenutzerRepository(Verbindungsfabrik db) : IBenutzerRepository
{
    public async Task<BenutzerMitHash?> FindeAktivenAsync(string benutzername, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            "SELECT id, benutzername, passwort_hash, rolle FROM benutzer WHERE benutzername = @name AND aktiv = 1",
            verbindung);
        befehl.Parameters.AddWithValue("@name", benutzername);
        await using var leser = await befehl.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await leser.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var rolle = Enum.Parse<Rolle>(leser.GetString(3), ignoreCase: true);
        return new BenutzerMitHash(new Benutzer(leser.GetInt32(0), leser.GetString(1), rolle), leser.GetString(2));
    }

    public async Task AktualisiereHashAsync(int benutzerId, string neuerHash, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand("UPDATE benutzer SET passwort_hash = @hash WHERE id = @id", verbindung);
        befehl.Parameters.AddWithValue("@hash", neuerHash);
        befehl.Parameters.AddWithValue("@id", benutzerId);
        await befehl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Creates a user if the name is free. Used by the lab seeder only.</summary>
    public async Task<bool> LegeAnFallsFehltAsync(string benutzername, string passwortHash, Rolle rolle, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            "INSERT IGNORE INTO benutzer (benutzername, passwort_hash, rolle, aktiv) VALUES (@name, @hash, @rolle, 1)",
            verbindung);
        befehl.Parameters.AddWithValue("@name", benutzername);
        befehl.Parameters.AddWithValue("@hash", passwortHash);
        befehl.Parameters.AddWithValue("@rolle", rolle.ToString());
        return await befehl.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }
}

public sealed class MariaDbRaumRepository(Verbindungsfabrik db) : IRaumRepository
{
    /// <summary>GetRaumAsync of the original: one active room by id.</summary>
    public async Task<Raum?> GetAktivenAsync(int raumId, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            "SELECT id, name, firewall_rule_uuid FROM raum WHERE id = @id AND aktiv = 1", verbindung);
        befehl.Parameters.AddWithValue("@id", raumId);
        await using var leser = await befehl.ExecuteReaderAsync(ct).ConfigureAwait(false);
        // MySqlConnector maps CHAR(36) to System.Guid by default, so read the value type-agnostic.
        return await leser.ReadAsync(ct).ConfigureAwait(false)
            ? new Raum(leser.GetInt32(0), leser.GetString(1),
                Convert.ToString(leser.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)!.ToLowerInvariant())
            : null;
    }
}

public sealed class MariaDbSperreSpeicher(Verbindungsfabrik db) : ISperreSpeicher
{
    private const string Spalten = "raum_id, gesperrt_seit, sperre_ende, gesperrt_von";

    public async Task<AktiveSperre?> GetAsync(int raumId, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand($"SELECT {Spalten} FROM sperre WHERE raum_id = @id", verbindung);
        befehl.Parameters.AddWithValue("@id", raumId);
        await using var leser = await befehl.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await leser.ReadAsync(ct).ConfigureAwait(false) ? Lese(leser) : null;
    }

    public async Task SetAsync(AktiveSperre sperre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sperre);
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            $"REPLACE INTO sperre ({Spalten}) VALUES (@raum, @seit, @ende, @von)", verbindung);
        befehl.Parameters.AddWithValue("@raum", sperre.RaumId);
        befehl.Parameters.AddWithValue("@seit", sperre.GesperrtSeit.UtcDateTime);
        befehl.Parameters.AddWithValue("@ende", sperre.Ende?.UtcDateTime);
        befehl.Parameters.AddWithValue("@von", sperre.GesperrtVon);
        await befehl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task EntferneAsync(int raumId, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand("DELETE FROM sperre WHERE raum_id = @id", verbindung);
        befehl.Parameters.AddWithValue("@id", raumId);
        await befehl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AktiveSperre>> GetAbgelaufeneAsync(DateTimeOffset jetzt, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            $"SELECT {Spalten} FROM sperre WHERE sperre_ende IS NOT NULL AND sperre_ende <= @jetzt", verbindung);
        befehl.Parameters.AddWithValue("@jetzt", jetzt.UtcDateTime);
        await using var leser = await befehl.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var liste = new List<AktiveSperre>();
        while (await leser.ReadAsync(ct).ConfigureAwait(false))
        {
            liste.Add(Lese(leser));
        }

        return liste;
    }

    private static AktiveSperre Lese(MySqlDataReader leser) => new(
        leser.GetInt32(0),
        Utc(leser.GetDateTime(1)),
        leser.IsDBNull(2) ? null : Utc(leser.GetDateTime(2)),
        leser.GetString(3));

    private static DateTimeOffset Utc(DateTime wert) => new(DateTime.SpecifyKind(wert, DateTimeKind.Utc));
}

public sealed class MariaDbProtokoll(Verbindungsfabrik db) : IProtokoll
{
    public async Task SchreibeAsync(ProtokollEintrag eintrag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eintrag);
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            "INSERT INTO protokoll (zeitpunkt, raum_id, akteur, aktion, details) VALUES (@zeit, @raum, @akteur, @aktion, @details)",
            verbindung);
        befehl.Parameters.AddWithValue("@zeit", eintrag.Zeitpunkt.UtcDateTime);
        befehl.Parameters.AddWithValue("@raum", eintrag.RaumId);
        befehl.Parameters.AddWithValue("@akteur", eintrag.Akteur);
        befehl.Parameters.AddWithValue("@aktion", eintrag.Aktion.ToString());
        befehl.Parameters.AddWithValue("@details", eintrag.Details);
        await befehl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProtokollEintrag>> LetzteAsync(int anzahl, CancellationToken ct = default)
    {
        await using var verbindung = await db.OeffneAsync(ct).ConfigureAwait(false);
        await using var befehl = new MySqlCommand(
            "SELECT zeitpunkt, raum_id, akteur, aktion, details FROM protokoll ORDER BY id DESC LIMIT @anzahl", verbindung);
        befehl.Parameters.AddWithValue("@anzahl", anzahl);
        await using var leser = await befehl.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var liste = new List<ProtokollEintrag>();
        while (await leser.ReadAsync(ct).ConfigureAwait(false))
        {
            liste.Add(new ProtokollEintrag(
                new DateTimeOffset(DateTime.SpecifyKind(leser.GetDateTime(0), DateTimeKind.Utc)),
                leser.IsDBNull(1) ? null : leser.GetInt32(1),
                leser.GetString(2),
                Enum.TryParse<ProtokollAktion>(leser.GetString(3), out var aktion) ? aktion : ProtokollAktion.Fehler,
                leser.IsDBNull(4) ? null : leser.GetString(4)));
        }

        return liste;
    }
}
