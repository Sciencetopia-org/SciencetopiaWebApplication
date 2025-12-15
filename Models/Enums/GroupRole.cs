namespace Sciencetopia.Models.Enums
{
    // Align to规范: group member roles include Owner/Admin/Member/Learner/TA
    public enum GroupRole : byte
    {
        Member = 0,
        Learner = 1,
        TA = 2,
        Admin = 3,
        // Manager kept as alias of Admin for backward compatibility
        Manager = Admin,
        Owner = 4
    }
}
