using System.Data;
using Microsoft.Data.SqlClient;
using WebApplication2.DTO;

namespace WebApplication2.Services;

public class AppointmentService : IAppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    public async Task<IEnumerable<AppointmentListDto>> GetAllAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(status) ? DBNull.Value : status;

        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrWhiteSpace(patientLastName) ? DBNull.Value : patientLastName;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return result;
    }

    public async Task<AppointmentDetailsDto?> GetByIdAsync(int idAppointment)
    {
        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName AS PatientFirstName,
                p.LastName AS PatientLastName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.IdDoctor,
                d.FirstName AS DoctorFirstName,
                d.LastName AS DoctorLastName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFirstName = reader.GetString(reader.GetOrdinal("PatientFirstName")),
            PatientLastName = reader.GetString(reader.GetOrdinal("PatientLastName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFirstName = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
            DoctorLastName = reader.GetString(reader.GetOrdinal("DoctorLastName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };
    }

    public async Task<(bool Success, int? NewId, string? Error, int StatusCode)> CreateAsync(CreateAppointmentRequestDto dto)
    {
        if (dto.AppointmentDate <= DateTime.Now)
            return (false, null, "Appointment date cannot be in the past.", 400);

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return (false, null, "Reason cannot be empty.", 400);

        if (dto.Reason.Length > 250)
            return (false, null, "Reason cannot be longer than 250 characters.", 400);

        if (!await ActivePatientExists(dto.IdPatient))
            return (false, null, "Patient does not exist or is inactive.", 400);

        if (!await ActiveDoctorExists(dto.IdDoctor))
            return (false, null, "Doctor does not exist or is inactive.", 400);

        if (await DoctorHasConflict(dto.IdDoctor, dto.AppointmentDate, null))
            return (false, null, "Doctor already has another appointment at this time.", 409);

        const string sql = """
            INSERT INTO dbo.Appointments
                (IdPatient, IdDoctor, AppointmentDate, Status, Reason, InternalNotes, CreatedAt)
            OUTPUT INSERTED.IdAppointment
            VALUES
                (@IdPatient, @IdDoctor, @AppointmentDate, @Status, @Reason, @InternalNotes, SYSUTCDATETIME());
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = "Scheduled";
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
        command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value = DBNull.Value;

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();

        return (true, Convert.ToInt32(result), null, 201);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> UpdateAsync(int idAppointment, UpdateAppointmentRequestDto dto)
    {
        var current = await GetCurrentAppointment(idAppointment);
        if (current == null)
            return (false, "Appointment not found.", 404);

        if (!await ActivePatientExists(dto.IdPatient))
            return (false, "Patient does not exist or is inactive.", 400);

        if (!await ActiveDoctorExists(dto.IdDoctor))
            return (false, "Doctor does not exist or is inactive.", 400);

        var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!allowedStatuses.Contains(dto.Status))
            return (false, "Status must be Scheduled, Completed or Cancelled.", 400);

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return (false, "Reason cannot be empty.", 400);

        if (dto.Reason.Length > 250)
            return (false, "Reason cannot be longer than 250 characters.", 400);

        if (dto.AppointmentDate <= DateTime.Now && dto.Status == "Scheduled")
            return (false, "Scheduled appointment cannot be set in the past.", 400);

        if (current.Status == "Completed" && current.AppointmentDate != dto.AppointmentDate)
            return (false, "Completed appointment cannot change its date.", 409);

        if (await DoctorHasConflict(dto.IdDoctor, dto.AppointmentDate, idAppointment))
            return (false, "Doctor already has another appointment at this time.", 409);

        const string sql = """
            UPDATE dbo.Appointments
            SET
                IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = dto.Status;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
        command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(dto.InternalNotes) ? DBNull.Value : dto.InternalNotes;

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();

        return (true, null, 200);
    }

    public async Task<(bool Success, string? Error, int StatusCode)> DeleteAsync(int idAppointment)
    {
        var current = await GetCurrentAppointment(idAppointment);
        if (current == null)
            return (false, "Appointment not found.", 404);

        if (current.Status == "Completed")
            return (false, "Completed appointment cannot be deleted.", 409);

        const string sql = """
            DELETE FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();

        return (true, null, 204);
    }

    private async Task<bool> ActivePatientExists(int idPatient)
    {
        const string sql = """
            SELECT 1
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient AND IsActive = 1;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = idPatient;

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    private async Task<bool> ActiveDoctorExists(int idDoctor)
    {
        const string sql = """
            SELECT 1
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    private async Task<bool> DoctorHasConflict(int idDoctor, DateTime appointmentDate, int? excludedAppointmentId)
    {
        const string sql = """
            SELECT 1
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND (@ExcludedId IS NULL OR IdAppointment <> @ExcludedId);
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointmentDate;
        command.Parameters.Add("@ExcludedId", SqlDbType.Int).Value =
            excludedAppointmentId.HasValue ? excludedAppointmentId.Value : DBNull.Value;

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    private async Task<AppointmentStateDto?> GetCurrentAppointment(int idAppointment)
    {
        const string sql = """
            SELECT IdAppointment, AppointmentDate, Status
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentStateDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status"))
        };
    }

    private class AppointmentStateDto
    {
        public int IdAppointment { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}