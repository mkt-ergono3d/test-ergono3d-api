using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InsoleApi.Database
{
    [Table("blog")]
    public class BlogEntity
    {

        [Column("title")]
        public required string Title { get; set; }

        [Column("detail")]
        public required string Detail { get; set; }

        [Column("create_time")]
        public DateTime CreateTime { get; set; }

        [Key]
        [Column("relative_url")]
        public required string RelativeUrl { get; set; }

        [Column("preview_img_base64")]
        public required string PreviewImgBase64 { get; set; }

        [Column("type")]
        public required string Type { get; set; }

        [Column("is_first")]
        public bool IsFirst { get; set; }

        [Column("content")]
        public required string Content { get; set; }

        [Column("seo")]
        public required string Seo { get; set; }

        [Column("is_index")]
        public bool IsIndex { get; set; }

    }
}
