using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>Tạo một đơn mới thẳng vào bảng chính (Bảng đơn mới là cửa sổ lọc các đơn này).</summary>
public interface INewOrderCreator
{
    Task<NewOrderResult> CreateAsync(NewOrderInput input);
}

public sealed class SqliteNewOrderCreator : INewOrderCreator
{
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;
    public SqliteNewOrderCreator(string dbPath, Func<DateTime>? clock = null) { _dbPath = dbPath; _clock = clock ?? (() => DateTime.Now); }

    public Task<NewOrderResult> CreateAsync(NewOrderInput input)
        => Task.Run(() => NewOrderService.Create(_dbPath, input, _clock()));
}

/// <summary>Bản giả cho demo/test: ghi vào kho demo trong bộ nhớ, không chạm đĩa.</summary>
public sealed class FakeNewOrderCreator : INewOrderCreator
{
    private readonly FakeDataService _data;
    public FakeNewOrderCreator(FakeDataService data) => _data = data;
    public List<NewOrderInput> Calls { get; } = new();

    public Task<NewOrderResult> CreateAsync(NewOrderInput input)
    {
        Calls.Add(input);
        return Task.FromResult(_data.AddDemoNewOrder(input));
    }
}

/// <summary>Hộp thoại nhập đơn mới. Trả null khi người dùng hủy.</summary>
public interface INewOrderDialogService
{
    NewOrderInput? Ask();
}

public sealed class FakeNewOrderDialogService : INewOrderDialogService
{
    public NewOrderInput? Next { get; set; }
    public int AskCount { get; private set; }
    public NewOrderInput? Ask() { AskCount++; return Next; }
}
