using AutoMapper;
using SmartInventory.Models.Organizations;

namespace SmartInventory.Models.Structure;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
        CreateMap<Organization, OrganizationEditViewModel>().ReverseMap();
        
        CreateMap<Building, BuildingEditViewModel>().ReverseMap();
        CreateMap<AccessCode, AccessCodeEditViewModel>().ReverseMap();
        
        CreateMap<Organization, OrganizationIndexViewModel>()
            .ForMember(dest => dest.BuildingsCount, 
                opt => opt.MapFrom(src => src.Buildings.Count))
            .ForMember(dest => dest.AccessCodesCount,
                opt => opt.MapFrom(src => src.AccessCodes.Count));
    }
}