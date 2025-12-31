using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineFetch.Api.Data;
using MineFetch.Entities.DTOs;
using MineFetch.Entities.Models;

namespace MineFetch.Api.Controllers;

/// <summary>
/// 群组管理 API 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GroupsController : ControllerBase
{
    private readonly ILogger<GroupsController> _logger;
    private readonly AppDbContext _dbContext;

    public GroupsController(ILogger<GroupsController> logger, AppDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// 同步群组列表（采集端上报）
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncGroups([FromBody] List<GroupSyncDto> groups, CancellationToken cancellationToken)
    {
        if (groups == null || groups.Count == 0)
            return BadRequest(new { error = "群组列表为空" });

        var inserted = 0;
        var updated = 0;

        foreach (var dto in groups)
        {
            var existing = await _dbContext.TelegramGroups
                .FirstOrDefaultAsync(g => g.Id == dto.GroupId, cancellationToken);

            if (existing == null)
            {
                // 新增群组
                var group = new TelegramGroup
                {
                    Id = dto.GroupId,
                    Title = dto.Title,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.TelegramGroups.Add(group);
                inserted++;
                _logger.LogInformation("新增群组: {Title} (ID: {GroupId})", dto.Title, dto.GroupId);
            }
            else if (existing.Title != dto.Title)
            {
                // 更新群组名称
                existing.Title = dto.Title;
                existing.UpdatedAt = DateTime.UtcNow;
                updated++;
                _logger.LogInformation("更新群组: {Title} (ID: {GroupId})", dto.Title, dto.GroupId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            inserted,
            updated,
            total = groups.Count
        });
    }

    /// <summary>
    /// 获取所有群组
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var groups = await _dbContext.TelegramGroups
            .OrderBy(g => g.Title)
            .Select(g => new
            {
                id = g.Id,
                title = g.Title,
                isActive = g.IsActive,
                createdAt = g.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { success = true, data = groups });
    }

    /// <summary>
    /// 切换群组启用状态
    /// </summary>
    [HttpPost("{groupId}/toggle")]
    public async Task<IActionResult> ToggleActive(long groupId, CancellationToken cancellationToken)
    {
        var group = await _dbContext.TelegramGroups.FindAsync([groupId], cancellationToken);
        
        if (group == null)
            return NotFound(new { error = "群组不存在" });

        group.IsActive = !group.IsActive;
        group.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, isActive = group.IsActive });
    }
}
