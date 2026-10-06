using APPQLXD.Core.Domain;

namespace APPQLXD.Core.Persistence;

/// <summary>Seed / đảm bảo cây nhiệm vụ mặc định (ảnh hạn mức).</summary>
public static class MissionSeeder
{
    public static void Ensure(AppDbContext db)
    {
        if (db.MissionGroups.Any())
            return;

        db.MissionGroups.AddRange(
            Group(SeedIds.MissionGroupStaff, "I", "Khối tham mưu", 1, false),
            Group(SeedIds.MissionGroupPolitics, "II", "Khối chính trị", 2, false),
            Group(SeedIds.MissionGroupLogistics, "III", "Khối hậu cần", 3, false),
            Group(SeedIds.MissionGroupTech, "IV", "Khối kỹ thuật", 4, false),
            Group(SeedIds.MissionGroupLoss, "V", "Hao hụt", 5, true));

        db.MissionTasks.AddRange(
            Task(SeedIds.MissionTaskCombat, SeedIds.MissionGroupStaff, "Tác chiến, SSCĐ", 1),
            Task(SeedIds.MissionTaskTraining, SeedIds.MissionGroupStaff, "H.luyện C.đầu", 2),
            Task(SeedIds.MissionTaskComms, SeedIds.MissionGroupStaff, "BĐảm KT T.Tin", 3),
            Task(SeedIds.MissionTaskPolicy, SeedIds.MissionGroupPolitics, "Chính sách", 1),
            Task(SeedIds.MissionTaskPropaganda, SeedIds.MissionGroupPolitics, "Tuyên huấn", 2),
            Task(SeedIds.MissionTaskParty, SeedIds.MissionGroupPolitics, "CTĐ - CTCT", 3),
            Task(SeedIds.MissionTaskSupply, SeedIds.MissionGroupLogistics, "Quân nhu", 1),
            Task(SeedIds.MissionTaskBarracks, SeedIds.MissionGroupLogistics, "Doanh trại", 2),
            Task(SeedIds.MissionTaskWeapons, SeedIds.MissionGroupTech, "Vũ khí đạn", 1),
            Task(SeedIds.MissionTaskVehicles, SeedIds.MissionGroupTech, "Ô tô xe, máy", 2),
            Task(SeedIds.MissionTaskLossRegular, SeedIds.MissionGroupLoss, "Hao hụt T.xuyên", 1),
            Task(SeedIds.MissionTaskLossReady, SeedIds.MissionGroupLoss, "Hao hụt SSCĐ", 2));

        db.SaveChanges();
    }

    private static MissionGroup Group(Guid id, string code, string name, int sort, bool loss) =>
        new() { Id = id, Code = code, Name = name, SortOrder = sort, IsLossGroup = loss };

    private static MissionTask Task(Guid id, Guid groupId, string name, int sort) =>
        new() { Id = id, GroupId = groupId, Name = name, SortOrder = sort, IsActive = true };
}
