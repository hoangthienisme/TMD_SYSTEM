using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMD.Models
{
	public class PasswordResetToken
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int Id { get; set; }

		public int UserId { get; set; } // Khóa ngoại liên kết với User.UserId

		[Required]
		[StringLength(6)]
		public string TokenCode { get; set; }

		public DateTime CreatedAt { get; set; }

		public DateTime ExpiresAt { get; set; }

		public DateTime ResendAvailableAt { get; set; }

		public int FailedAttempts { get; set; } = 0;

		public bool IsUsed { get; set; } = false;

		public DateTime? LockoutUntil { get; set; }

		[ForeignKey("UserId")]
		public virtual User User { get; set; }
	}
}