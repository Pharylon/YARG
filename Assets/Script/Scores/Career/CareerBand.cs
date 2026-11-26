using System;
using SQLite;

namespace YARG.Scores
{
    // A band: a named group of profiles that owns career saves (CareerSaves.BandId). Saves without a
    // band belong to the exact set of profiles that played them (CareerSaveProfiles), as before.
    [Table("Bands")]
    public class CareerBand
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string Hometown { get; set; }
        public DateTime CreatedAt { get; set; }

        // Deleting a band is a soft delete: its saves and their history stay in the database.
        public bool IsDeleted { get; set; }
    }
}
