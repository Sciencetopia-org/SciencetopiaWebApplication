public class StudyGroup
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    public List<GroupMember>? MemberIds { get; set; }
    public string Role { get; set; } = string.Empty; // Add 'required' modifier and initialize with an empty string
    public string? ImageUrl { get; set; }
    public string? Status { get; set; }
    // Other properties like members, posts, etc.

    public StudyGroup()
    {
        MemberIds = new List<GroupMember>();
    }
}

public class GroupMember
{
    public string? Id { get; set; }
    public string? Role {get; set;}
}

public class InviteMemberRequest
{
    public string? StudyGroupId { get; set; }
    public string? MemberId { get; set; }
}

public class ApproveJoinRequest
{
    public string? UserId { get; set; }
    public string? StudyGroupId { get; set; }
}

public class DeleteMemberRequest
{
    public string? StudyGroupId { get; set; }
    public string? MemberId { get; set; }
}

public class TransferManagerRoleRequest
{
    public string? StudyGroupId { get; set; }
    public string? NewManagerId { get; set; }
}

public class RenameGroupRequest
{
    public string? StudyGroupId { get; set; }
    public string? NewName { get; set; }
}

public class EditDescriptionRequest
{
    public string? StudyGroupId { get; set; }
    public string? NewDescription { get; set; }
}

public class EditProfilePictureRequest
{
    public string? StudyGroupId { get; set; }
    public string? NewProfilePictureUrl { get; set; }
}

public class JoinRequest
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public string AppliedOn { get; set; } // Assuming you store the applied date as a string
    public string AvatarUrl { get; set; } // Assuming you have an avatar URL for the requester
}

public class ActivityLog
{
    public string Id { get; set; } // Unique ID for the log
    public string Message { get; set; }
    public string Date { get; set; } // Assuming you store the date as a string
}
