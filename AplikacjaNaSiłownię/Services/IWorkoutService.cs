using AplikacjaNaSiłownię.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AplikacjaNaSiłownię.Services
{
    public interface IWorkoutService
    {
        Task<bool> AddWorkoutAsync(PlannedWorkout workout, string creatorId, string userRole);
        Task<bool> UpdateWorkoutAsync(PlannedWorkout workout, string editorId, string userRole);
        Task<bool> DeleteWorkoutAsync(int workoutId, string editorId, string userRole);
        Task<List<PlannedWorkout>> GetClientWorkoutsAsync(string clientId);
    }
}
