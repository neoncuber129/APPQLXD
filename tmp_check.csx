using APPQLXD.Core.Calculations;
var cases = new (decimal actual, decimal amount)[] { (100m, 2_000_050m), (7m, 100_000m), (123m, 2_456_789m), (50m, 1_000_001m), (999m, 20_000_000m), (1m, 15_001m) };
foreach (var (actual, amount) in cases) {
  var pairs = ImportLotSplitter.FloorPairs(actual, amount);
  Console.WriteLine($"actual={actual} amount={amount} pairs={pairs.Count}");
  foreach (var p in pairs) {
    var okQty = p.Quantity1 + p.Quantity2 == actual;
    var okAmt = p.Amount1 + p.Amount2 == amount;
    var okExact = p.Amount1 == p.Price1 * p.Quantity1 && p.Amount2 == p.Price2 * p.Quantity2;
    if (!okQty || !okAmt || !okExact) Console.WriteLine($"  FAIL {p}");
  }
}
