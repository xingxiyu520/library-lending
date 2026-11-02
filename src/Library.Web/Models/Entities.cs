using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Library.Web.Models;
/// <summary>馆藏实体，ISBN唯一；总册数与未归还借阅共同决定可借数量。</summary>
public class Book
{
    /// <summary>实体或结果的唯一编号，用于查找和稳定排序。</summary>
    public int Id { get; set; }

    /// <summary>统一规范化的ISBN，数据库使用唯一索引防止重复。</summary>
    [MaxLength(32)]
    public string Isbn { get; set; } = "";

    /// <summary>图书标题，长度受限制且由Razor默认编码显示。</summary>
    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>作者姓名，用于目录展示和关键词匹配。</summary>
    [MaxLength(100)]
    public string Author { get; set; } = "";

    /// <summary>可选图书分类，空值不影响借阅规则。</summary>
    [MaxLength(60)]
    public string? Category { get; set; }
    /// <summary>馆藏总册数，非负且不得少于未归还数量。</summary>
    public int TotalCopies { get; set; }
    /// <summary>当前实体关联的借阅集合，用于计算未归还数量。</summary>
    public List<Loan> Loans { get; set; } = [];
}

/// <summary>读者档案实体，通过唯一UserId关联Identity账号，并保留借阅历史关系。</summary>
public class Reader
{
    /// <summary>实体或结果的唯一编号，用于查找和稳定排序。</summary>
    public int Id { get; set; }

    /// <summary>关联Identity账号的唯一标识，由服务端维护。</summary>
    [MaxLength(255)]
    public string UserId { get; set; } = "";
    /// <summary>关联的Identity登录账号，档案删除受事务保护。</summary>
    public IdentityUser User { get; set; } = null !;

    /// <summary>读者显示姓名，不用于决定登录身份或授权。</summary>
    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>规范化邮箱，同时作为登录名并满足唯一性要求。</summary>
    [MaxLength(256)]
    public string Email { get; set; } = "";

    /// <summary>可选联系电话，不用于授权或身份归属。</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }
}

/// <summary>借阅实体，关联图书和读者，保存借出、应还、实际归还日期与幂等请求标识。</summary>
public class Loan
{
    /// <summary>实体或结果的唯一编号，用于查找和稳定排序。</summary>
    public int Id { get; set; }
    /// <summary>当前借阅关联的图书编号，更正日期时不可修改。</summary>
    public int BookId { get; set; }
    /// <summary>关联图书导航属性，由EF按需要加载。</summary>
    public Book Book { get; set; } = null !;
    /// <summary>当前借阅关联的读者档案编号，不信任客户端替换个人查询身份。</summary>
    public int ReaderId { get; set; }
    /// <summary>关联读者导航属性，管理员列表可以展示姓名。</summary>
    public Reader Reader { get; set; } = null !;
    /// <summary>借出业务日期，不得晚于UTC+8当天。</summary>
    public DateOnly BorrowDate { get; set; }
    /// <summary>应还日期，不得早于借出日期。</summary>
    public DateOnly DueDate { get; set; }
    /// <summary>实际归还日期，空值表示未归还；登记后更正入口不能覆盖。</summary>
    public DateOnly? ReturnDate { get; set; }

    /// <summary>登记请求的唯一标识，用于识别重复提交。</summary>
    [MaxLength(36)]
    public string RequestId { get; set; } = "";
}
