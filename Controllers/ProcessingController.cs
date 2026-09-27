using Microsoft.AspNetCore.Mvc;
using TestCaseSDA.Models;
using TestCaseSDA.Services;

namespace TestCaseSDA.Controllers;

[ApiController]
[Route("api/process")]
public sealed class ProcessingController(PageProcessingService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProcessResponse>> Process(
        ProcessRequest request, CancellationToken cancellationToken)
    {
        var response = await service.ProcessAsync(request, cancellationToken);
        var status = response.IsError == 0 ? StatusCodes.Status200OK
            : response.ErrorCode is "DATABASE_ERROR" or "INTERNAL_ERROR"
                ? StatusCodes.Status500InternalServerError
                : StatusCodes.Status400BadRequest;
        return StatusCode(status, response);
    }
}
