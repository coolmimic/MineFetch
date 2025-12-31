using Microsoft.AspNetCore.Mvc;
using MineFetch.Api.Services;
using MineFetch.Entities.DTOs;

namespace MineFetch.Api.Controllers;

/// <summary>
/// 开奖数据 API 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LotteryController : ControllerBase
{
    private readonly ILogger<LotteryController> _logger;
    private readonly LotteryService _lotteryService;

    public LotteryController(ILogger<LotteryController> logger, LotteryService lotteryService)
    {
        _logger = logger;
        _lotteryService = lotteryService;
    }

    /// <summary>
    /// 采集端上报开奖结果
    /// </summary>
    [HttpPost("report")]
    public async Task<IActionResult> Report([FromBody] LotteryReportDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(dto.PeriodId))
            return BadRequest(new { error = "期号不能为空" });

        if (dto.DiceNumber < 1 || dto.DiceNumber > 6)
            return BadRequest(new { error = "骰子号码必须在 1-6 之间" });

        var result = await _lotteryService.ReportAsync(dto, cancellationToken);
        
        if (result == null)
            return Ok(new { success = true, message = "期号已存在" });

        return Ok(new { 
            success = true, 
            data = new
            {
                id = result.Id,
                periodId = result.PeriodId,
                diceNumber = result.DiceNumber,
                size = result.Size.ToString(),
                parity = result.Parity.ToString()
            }
        });
    }

    /// <summary>
    /// 获取开奖历史
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] long? groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var results = await _lotteryService.GetHistoryAsync(groupId, page, pageSize, cancellationToken);

        return Ok(new
        {
            success = true,
            data = results.Select(r => new
            {
                id = r.Id,
                periodId = r.PeriodId,
                diceNumber = r.DiceNumber,
                size = r.Size.ToString(),
                parity = r.Parity.ToString(),
                groupId = r.GroupId,
                collectedAt = r.CollectedAt
            })
        });
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    [HttpGet("stats/{groupId}")]
    public async Task<IActionResult> GetStats(
        long groupId,
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        if (count < 1) count = 50;
        if (count > 500) count = 500;

        var stats = await _lotteryService.GetStatsAsync(groupId, count, cancellationToken);
        return Ok(new { success = true, data = stats });
    }
}
