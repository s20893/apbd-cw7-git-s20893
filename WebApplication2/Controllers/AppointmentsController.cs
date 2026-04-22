using Microsoft.AspNetCore.Mvc;
using WebApplication2.DTO;
using WebApplication2.Services;

namespace WebApplication2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;

    public AppointmentsController(IAppointmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var result = await _service.GetAllAsync(status, patientLastName);
        return Ok(result);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetById(int idAppointment)
    {
        var result = await _service.GetByIdAsync(idAppointment);

        if (result == null)
        {
            return NotFound(new ErrorResponseDto
            {
                Message = "Appointment not found."
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequestDto dto)
    {
        var result = await _service.CreateAsync(dto);

        if (!result.Success)
        {
            return result.StatusCode switch
            {
                400 => BadRequest(new ErrorResponseDto { Message = result.Error! }),
                409 => Conflict(new ErrorResponseDto { Message = result.Error! }),
                _ => BadRequest(new ErrorResponseDto { Message = result.Error ?? "Unknown error." })
            };
        }

        return CreatedAtAction(nameof(GetById), new { idAppointment = result.NewId }, new { idAppointment = result.NewId });
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> Update(int idAppointment, [FromBody] UpdateAppointmentRequestDto dto)
    {
        var result = await _service.UpdateAsync(idAppointment, dto);

        if (!result.Success)
        {
            return result.StatusCode switch
            {
                400 => BadRequest(new ErrorResponseDto { Message = result.Error! }),
                404 => NotFound(new ErrorResponseDto { Message = result.Error! }),
                409 => Conflict(new ErrorResponseDto { Message = result.Error! }),
                _ => BadRequest(new ErrorResponseDto { Message = result.Error ?? "Unknown error." })
            };
        }

        return Ok();
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> Delete(int idAppointment)
    {
        var result = await _service.DeleteAsync(idAppointment);

        if (!result.Success)
        {
            return result.StatusCode switch
            {
                404 => NotFound(new ErrorResponseDto { Message = result.Error! }),
                409 => Conflict(new ErrorResponseDto { Message = result.Error! }),
                _ => BadRequest(new ErrorResponseDto { Message = result.Error ?? "Unknown error." })
            };
        }

        return NoContent();
    }
}