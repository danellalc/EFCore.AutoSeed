using EFCore.AutoSeed;
using EFCore.AutoSeed.Examples.CustomerOrders;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("== AutoSeedExplainAsync: work out the plan without writing anything ==");
Console.WriteLine();

using (CustomerOrderContext explainContext = new(databaseName: "CustomerOrders-Explain"))
{
    await explainContext.Database.EnsureCreatedAsync();

    AutoSeedExplainResult plan = await explainContext.AutoSeedExplainAsync(seed: 42, scale: 1_000);
    Console.WriteLine(plan.ToReport());
    Console.WriteLine($"Rows written so far: {await explainContext.Customers.CountAsync()} (explain never writes).");
}

Console.WriteLine();
Console.WriteLine("== AutoSeedAsync: realistic data at scale ==");
Console.WriteLine();

using (CustomerOrderContext context = new(databaseName: "CustomerOrders"))
{
    await context.Database.EnsureCreatedAsync();

    IReadOnlyDictionary<string, int> rowCounts = await context.AutoSeedAsync(seed: 42, scale: 1_000);

    int customerCount = rowCounts[typeof(Customer).FullName!];
    int orderCount = rowCounts[typeof(Order).FullName!];
    int orderItemCount = rowCounts[typeof(OrderItem).FullName!];

    Console.WriteLine($"Seeded {customerCount:N0} customers, {orderCount:N0} orders, {orderItemCount:N0} order items.");
    Console.WriteLine();

    Customer sampleCustomer = await context.Customers
        .Include(customer => customer.Orders)
        .OrderByDescending(customer => customer.Orders.Count)
        .FirstAsync();

    Console.WriteLine($"Busiest customer: {sampleCustomer.FirstName} {sampleCustomer.LastName} <{sampleCustomer.Email}>");
    Console.WriteLine($"  Phone: {sampleCustomer.Phone}, postal code: {sampleCustomer.PostalCode}");
    Console.WriteLine($"  Placed {sampleCustomer.Orders.Count} order(s) since {sampleCustomer.CreatedAt:yyyy-MM-dd}");

    foreach (Order order in sampleCustomer.Orders.OrderBy(order => order.CreatedAt).Take(3))
    {
        Console.WriteLine($"    Order #{order.Id}: {order.Total:0.00} placed {order.CreatedAt:yyyy-MM-dd}");
    }
}

Console.WriteLine();
Console.WriteLine("== AutoSeedCoverageAsync: the smallest dataset that touches every code path ==");
Console.WriteLine();

using (CustomerOrderContext coverageContext = new(databaseName: "CustomerOrders-Coverage"))
{
    await coverageContext.Database.EnsureCreatedAsync();

    IReadOnlyDictionary<string, int> coverageRowCounts = await coverageContext.AutoSeedCoverageAsync();
    int totalRows = coverageRowCounts.Values.Sum();

    Console.WriteLine($"Seeded {totalRows} row(s) total, no seed or scale to configure: row counts are structural.");
    foreach (KeyValuePair<string, int> entry in coverageRowCounts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {entry.Key.Split('.')[^1]}: {entry.Value} row(s)");
    }
}
