using WebApplication2.DTO;

namespace WebApplication2.Services;

public interface IAppointmentService
{
    Task<IEnumerable<AppointmentListDto>> GetAllAsync(string? status, string? patientLastName);
    Task<AppointmentDetailsDto?> GetByIdAsync(int idAppointment);
    Task<(bool Success, int? NewId, string? Error, int StatusCode)> CreateAsync(CreateAppointmentRequestDto dto);
    Task<(bool Success, string? Error, int StatusCode)> UpdateAsync(int idAppointment, UpdateAppointmentRequestDto dto);
    Task<(bool Success, string? Error, int StatusCode)> DeleteAsync(int idAppointment);
}