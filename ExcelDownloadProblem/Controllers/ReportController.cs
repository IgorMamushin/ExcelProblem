using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDownloadProblem.Controllers;

[ApiController]
[Route("reports")]
public class ReportController : ControllerBase
{
    [HttpGet("get-report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReport([FromQuery][Required] DateTime from)
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ExcelDownloadProblem.Resources.SomeExcelReport.xlsx");

        var memoryStream = new MemoryStream();

        HttpContext.Response.ContentType = MediaTypeNames.Application.Json;
        await stream!.CopyToAsync(memoryStream, HttpContext.RequestAborted);

        return File(memoryStream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Report.xlsx");
    }
}