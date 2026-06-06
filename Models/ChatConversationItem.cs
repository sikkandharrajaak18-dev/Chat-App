namespace Chat_App.Models
{
    public class ChatConversationItem
    {
        public bool IsGroup { get; set; }
        public int Id { get; set; }
        public string DisplayName { get; set; }
        public string ProfileImagePath { get; set; }
        public string LastMessageText { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public string FileType { get; set; }
        public string FileName { get; set; }
        public int UnreadCount { get; set; }
        public bool IsOnline { get; set; }
        public int MemberCount { get; set; }
        public string FriendStatus { get; set; } = "None";
        public bool IsAdmin { get; set; }
    }
}
