import sqlite3
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
con = sqlite3.connect(
    r"file:C:/Users/AD/source/repos/APPQLXD/APPQLXD/bin/Debug/net10.0-windows/appqlxd.db?mode=ro",
    uri=True,
)
c = con.cursor()
doc_id = "365D93E1-8EAF-4B1B-9771-68A5923997E4"
print("exact", c.execute("SELECT Id FROM Documents WHERE Id=?", (doc_id,)).fetchall())
print("upper", c.execute("SELECT Id FROM Documents WHERE upper(Id)=upper(?)", (doc_id,)).fetchall())
print("like", c.execute("SELECT Id, Kind, ItemName FROM Documents WHERE Id LIKE '365D93E1%'" ).fetchall())
print("movement docids for oil xuat:")
for r in c.execute(
    """SELECT m.DocumentId, d.Id, d.Kind, d.ItemName
       FROM StockMovements m
       LEFT JOIN Documents d ON d.Id=m.DocumentId OR lower(d.Id)=lower(m.DocumentId)
       WHERE m.Reason='Xuất tiêu thụ' AND m.SignedQuantity=-150"""
):
    print(r)

# all orphaned movements globally for Xuất tiêu thụ
print("\nglobal orphaned consumption movements:")
for r in c.execute(
    """SELECT m.DocumentId, m.SignedQuantity, m.WarehouseId, m.Reason
       FROM StockMovements m
       WHERE m.Reason='Xuất tiêu thụ'
         AND NOT EXISTS (SELECT 1 FROM Documents d WHERE d.Id=m.DocumentId OR lower(d.Id)=lower(m.DocumentId))
       LIMIT 20"""
):
    print(r)
