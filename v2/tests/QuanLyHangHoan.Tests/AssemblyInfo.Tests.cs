using Xunit;

// Các test giao diện WPF tạo System.Windows.Application và luồng STA riêng: chạy song song giữa các lớp test làm chúng tranh nhau
// ("Cannot create more than one System.Windows.Application instance in the same AppDomain"), đo được 4/12 lần chạy đỏ.
// Tắt song song giữa các lớp test (mỗi lớp vẫn chạy tuần tự nội bộ). Bộ test vẫn chỉ vài giây.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
