using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Library.Web.Models;
/// <summary>登录表单输入；邮箱与密码必须验证，跳转地址须由控制器判断是否站内。</summary>
public class LoginInput
{
    /// <summary>规范化邮箱，同时作为登录名并满足唯一性要求。</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    /// <summary>输入或测试时使用的密码；正式存储由Identity生成哈希，不写入公共仓库。</summary>
    [Required, DataType(DataType.Password), StringLength(256)]
    public string Password { get; set; } = "";
    /// <summary>登录后候选跳转地址，使用前必须验证为站内URL。</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>图书新增和编辑输入，服务端验证必填字段、长度及非负整数总册数。</summary>
public class BookInput
{
    /// <summary>统一规范化的ISBN，数据库使用唯一索引防止重复。</summary>
    [Required, StringLength(32), Display(Name = "ISBN")]
    public string Isbn { get; set; } = "";

    /// <summary>图书标题，长度受限制且由Razor默认编码显示。</summary>
    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    /// <summary>作者姓名，用于目录展示和关键词匹配。</summary>
    [Required, StringLength(100)]
    public string Author { get; set; } = "";

    /// <summary>可选图书分类，空值不影响借阅规则。</summary>
    [StringLength(60)]
    public string? Category { get; set; }

    /// <summary>馆藏总册数，非负且不得少于未归还数量。</summary>
    [Required, Range(0, int.MaxValue), Display(Name = "Total copies")]
    public int? TotalCopies { get; set; }
}

/// <summary>新建读者及登录账号的输入；不提供角色字段以避免表单提权。</summary>
public class ReaderInput
{
    /// <summary>读者显示姓名，不用于决定登录身份或授权。</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>规范化邮箱，同时作为登录名并满足唯一性要求。</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    /// <summary>可选联系电话，不用于授权或身份归属。</summary>
    [StringLength(30)]
    public string? Phone { get; set; }

    /// <summary>输入或测试时使用的密码；正式存储由Identity生成哈希，不写入公共仓库。</summary>
    [Required, StringLength(256, MinimumLength = 8), RegularExpression(@"(?=.*[A-Za-z])(?=.*[0-9]).+", ErrorMessage = "Password must contain a letter and a number."), DataType(DataType.Password), Display(Name = "Initial password")]
    public string Password { get; set; } = "";
}

/// <summary>登记借阅的输入和下拉选项；RequestId必须由登记页面显式生成。</summary>
public class LoanInput
{
    /// <summary>当前借阅关联的图书编号，更正日期时不可修改。</summary>
    [Required, Range(1, int.MaxValue), Display(Name = "Book")]
    public int? BookId { get; set; }

    /// <summary>当前借阅关联的读者档案编号，不信任客户端替换个人查询身份。</summary>
    [Required, Range(1, int.MaxValue), Display(Name = "Reader")]
    public int? ReaderId { get; set; }

    /// <summary>借出业务日期，不得晚于UTC+8当天。</summary>
    [Required, DataType(DataType.Date), Display(Name = "Borrow date")]
    public DateOnly? BorrowDate { get; set; }

    /// <summary>应还日期，不得早于借出日期。</summary>
    [Required, DataType(DataType.Date), Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    /// <summary>登记请求的唯一标识，用于识别重复提交。</summary>
    [Required, StringLength(36)]
    public string RequestId { get; set; } = "";

    /// <summary>图书查询集合或表单选项，实际权限由控制器和服务端查询保证。</summary>
    [ValidateNever]
    public List<SelectListItem> Books { get; set; } = [];

    /// <summary>读者查询集合或表单选项，读者端没有全量访问入口。</summary>
    [ValidateNever]
    public List<SelectListItem> Readers { get; set; } = [];
}

/// <summary>目录行的只读投影，包含总册数和按未归还记录计算的可借数量。</summary>
public record BookRow(int Id, string Isbn, string Title, string Author, string? Category, int TotalCopies, int AvailableCopies);
/// <summary>读者档案更正输入，不接受密码或角色变更；邮箱同步更新登录账号。</summary>
public class ReaderEditInput
{
    /// <summary>读者显示姓名，不用于决定登录身份或授权。</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>规范化邮箱，同时作为登录名并满足唯一性要求。</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    /// <summary>可选联系电话，不用于授权或身份归属。</summary>
    [StringLength(30)]
    public string? Phone { get; set; }
}

/// <summary>归还表单输入；必填日期还须通过借出日与业务当天边界校验。</summary>
public class ReturnInput
{
    /// <summary>归还操作要求填写实际归还日；范围由业务服务结合借出日期校验。</summary>
    [Required, DataType(DataType.Date), Display(Name = "Return date")]
    public DateOnly? ReturnDate { get; set; }
}

/// <summary>馆藏列表页面模型，携带当前关键词和白名单排序以回填查询控件。</summary>
public record BookCatalog(IReadOnlyList<BookRow> Books, string Query, string Sort);
/// <summary>读者列表页面模型，保存查询结果及当前搜索、排序条件。</summary>
public record ReaderDirectory(IReadOnlyList<Reader> Readers, string Query, string Sort);
// 日期更正输入刻意排除图书、读者及归还日期，避免越权字段被模型绑定。
/// <summary>借阅日期更正输入，只允许绑定借出日与应还日，保护图书、读者及归还状态。</summary>
public class LoanEditInput
{
    /// <summary>借出业务日期，不得晚于UTC+8当天。</summary>
    [Required, DataType(DataType.Date), Display(Name = "Borrow date")]
    public DateOnly? BorrowDate { get; set; }

    /// <summary>应还日期，不得早于借出日期。</summary>
    [Required, DataType(DataType.Date), Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }
}

// 列表只输出显示所需字段；读者列表不加载其他人的姓名。
/// <summary>借阅列表的最小展示投影，读者页面不携带其他人的姓名。</summary>
public record LoanRow(int Id, string Title, DateOnly BorrowDate, DateOnly DueDate, DateOnly? ReturnDate, string Status, string? ReaderName = null);
// 查询参数先归一化为白名单值，再构建参数化的数据库查询。
/// <summary>借阅查询条件；关键词去除首尾空白，状态与排序必须落入允许集合。</summary>
public record LoanFilters(string Query = "", string Status = "all", string Sort = "borrow_desc")
{
    /// <summary>将关键词与白名单条件规范化；非法值安全回退，身份范围不由筛选决定。</summary>
    public static LoanFilters Normalize(string? query, string? status, string? sort) => new((query ?? "").Trim(), status is "outstanding" or "returned" or "overdue" ? status : "all", sort is "borrow" or "due" or "due_desc" ? sort : "borrow_desc");
}

/// <summary>借阅页面模型，区分管理员、读者和缺失档案状态，供列表与共用筛选表单使用。</summary>
public record LoanListPage(IReadOnlyList<LoanRow> Loans, LoanFilters Filters, bool IsReader = false, bool MissingProfile = false);
