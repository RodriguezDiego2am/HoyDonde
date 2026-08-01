using Google.Cloud.Firestore;

namespace HoyDonde.API.Converters
{
    // Google.Cloud.Firestore no tiene un converter propio para System.Decimal (no existe un tipo
    // Decimal en Firestore); sin este converter, cualquier lectura/escritura real de un tipo que
    // tenga una propiedad decimal falla con "Unable to create converter for type System.Decimal".
    // Se persiste como double y se reconvierte a decimal al leer.
    public class DecimalFirestoreConverter : IFirestoreConverter<decimal>
    {
        public object ToFirestore(decimal value) => (double)value;

        public decimal FromFirestore(object value) => value switch
        {
            double d => (decimal)d,
            long l => l,
            int i => i,
            _ => System.Convert.ToDecimal(value)
        };
    }
}
