namespace Sciencetopia.Models.Enums
{
    public enum PlanRole : byte
    {
        Viewer = 0,
        Commenter = 1,
        Editor = 2,
        Owner = 3 // 一般不通过此表设置，所有权以 StudyPlans.OwnerUserId 为准（如有）
    }
}

