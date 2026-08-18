namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2B — Login page (Pages/Login.razor). Hero panel + form + inline
// auth error messages (was Login's own LocaliseError switch — NOT one of the
// 7 locked *ErrorLocaliser.cs, so it migrates here).
public sealed partial class TranslationCatalog
{
    private void RegisterLogin()
    {
        //     key                     vi                                              en
        Add("login.pagetitle",     "Đăng nhập — CCL MES",                           "Log in — CCL MES");
        Add("login.slogan1",       "Làm hôm nay,",                                  "Make today,");
        Add("login.slogan2",       "giao ngày mai.",                                "ship tomorrow.");
        Add("login.desc",          "Hệ thống điều hành sản xuất của CCL Design Vietnam — điều hành sản xuất realtime từ NPI đến xuất xưởng.",
                                   "Manufacturing Execution System of CCL Design Vietnam — real-time production control from NPI to shipment.");
        Add("login.feat1",         "Truy xuất nguồn gốc từng Work Order",           "Trace every Work Order");
        Add("login.feat2",         "IPQC / FQC / OQC theo thời gian thực",          "IPQC / FQC / OQC in real time");
        Add("login.feat3",         "Bảng điều khiển máy & tiến độ dây chuyền",      "Machine dashboard & line progress");
        Add("login.stat.roles",    "Vai trò",                                       "Roles");
        Add("login.stat.phases",   "Giai đoạn MES",                                 "MES phases");
        Add("login.stat.traceable","Truy xuất",                                     "Traceable");
        Add("login.footbrand",     "CCL Design Vietnam · Sản xuất",                 "CCL Design Vietnam · Manufacturing");
        Add("login.welcome",       "Chào mừng trở lại",                          "Welcome back");
        Add("login.sub",           "Đăng nhập CCL MES",                             "Sign in to CCL MES");
        Add("login.username",      "Tên đăng nhập",                                 "Username");
        Add("login.password",      "Mật khẩu",                                      "Password");
        Add("login.submit",        "Đăng nhập",                                     "Sign in");
        Add("login.submitting",    "Đang đăng nhập…",                               "Logging in…");
        Add("login.noaccount",     "Chưa có tài khoản?",                            "Don't have an account?");
        Add("login.contact",       "Liên hệ nhóm NPI",                              "Contact the NPI team");

        // Inline auth errors (server code → message).
        Add("login.err.invalid",   "Sai tên đăng nhập hoặc mật khẩu.",              "Incorrect username or password.");
        Add("login.err.missing",   "Vui lòng điền đầy đủ các trường.",              "Please fill in all fields.");
        Add("login.err.network",   "Không kết nối được máy chủ. Kiểm tra mạng và thử lại.",
                                   "Could not connect to the server. Check your network and try again.");
        Add("login.err.timeout",   "Máy chủ phản hồi quá lâu. Vui lòng thử lại.",   "The server took too long to respond. Please try again.");
        Add("login.err.generic",   "Đăng nhập thất bại. Vui lòng thử lại.",         "Login failed. Please try again.");
    }
}
