using System;

namespace HoyDonde.API.Services
{
    // Zona horaria funcional de HoyDonde para agrupar la serie temporal de ventas (docs/api-mvp-plan.md
    // §11): America/Argentina/Buenos_Aires, sin DST desde 2009 (offset fijo UTC-3), pero resuelta
    // vía TimeZoneInfo (no un offset hardcodeado) para dejar que el runtime maneje cualquier regla
    // histórica/futura. .NET 8 con ICU (el default, sin globalización invariante) entiende el id
    // IANA tanto en Linux como en Windows; el id de Windows queda como respaldo explícito por si
    // corre bajo un runtime sin ese mapeo, y un offset fijo como último respaldo defensivo -nunca
    // se agrega NodaTime ni otra dependencia grande solo para esto-.
    public static class ArgentinaTimeZoneProvider
    {
        public static readonly TimeZoneInfo TimeZone = Resolve();

        private static TimeZoneInfo Resolve()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            return TimeZoneInfo.CreateCustomTimeZone(
                "Argentina-Fallback-UTC-3", TimeSpan.FromHours(-3), "Argentina (UTC-3)", "Argentina (UTC-3)");
        }

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

        public static DateTime ToUtc(DateTime local) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);
    }
}
