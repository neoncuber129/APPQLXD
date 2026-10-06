using APPQLXD.Core.Models;

namespace APPQLXD.Core.Services;

public static class ExportFuelChoice
{
    public static LotOption? Pick(IEnumerable<LotOption> lots, IEnumerable<ItemRow> items, Guid groupId, Guid? preferredItemId)
    {
        var ids = items.Where(x => x.GroupId == groupId).Select(x => x.Id).ToHashSet();
        var available = lots.Where(x => x.ItemId is Guid id && ids.Contains(id) && x.Quantity > 0).ToList();
        if (available.Count == 0)
            return null;
        var named = available.Where(x => x.ItemId == preferredItemId).ToList();
        var pool = named.Count > 0 ? named : available;
        return pool.OrderByDescending(x => x.Quantity).ThenBy(x => x.ItemName).ThenBy(x => x.UnitPrice).First();
    }
}
