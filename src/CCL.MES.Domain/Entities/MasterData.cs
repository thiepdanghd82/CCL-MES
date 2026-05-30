namespace CCL.MES.Domain.Entities;

public class Customer : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}

public class Product : BaseEntity
{
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
