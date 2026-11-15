using System;
using SQLite;

namespace YARG.Scores
{
    // One profile in a band. Removing a member deletes the row.
    [Table("BandMembers")]
    public class CareerBandMember
    {
        [PrimaryKey][AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public Guid BandId { get; set; }

        [Indexed]
        public Guid ProfileId { get; set; }

        public DateTime JoinedAt { get; set; }
    }
}
