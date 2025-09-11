using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLM
{
    public class MetodiCsv
    {
        static CsvConfiguration csvConfigurazione = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };

        public static List<T> letturaCsv<T>(string percorso) where T : class
        {
            List<T> listaTempCsv = new List<T>();

            using (var stream = new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            using (var csvReader = new CsvReader(reader, csvConfigurazione))
            {
                listaTempCsv = csvReader.GetRecords<T>().ToList();
            }

            return listaTempCsv;
        }


        public static void scritturaCsv<T>(string percorso, List<T> lista)
        {
            using (var writer = new StreamWriter(percorso))
            using (var csv = new CsvWriter(writer, csvConfigurazione))
            {
                csv.WriteRecords(lista);
            }
        }
    }
}
